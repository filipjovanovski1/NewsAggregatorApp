"""Builds the result payload the pipeline POSTs back to .NET.

Implements §2 of `docs/integration-plan.md`. Pure functions, no I/O: the whole
module is testable without a browser, an Ollama, or a network, which is the
point of it being separate from `main.py`.

The one structural rule, from §1: run facts nest under `relevance`, they do not
sit next to `domain`. `aljazeera.com` scoring medium on a North Macedonia run
is a fact about that *scope*, not about the site — the same domain will be
discovered again for Sarajevo and Doha with a different score each time, and
flattening the two kinds of fact together means the second city silently
overwrites the first city's score.
"""

from __future__ import annotations

import re
from datetime import datetime, timezone

from src.config import (
    DOMAIN_GENERIC_LABELS,
    NAME_DOMAIN_MATCH_MIN_CHARS,
    NAME_SEPARATOR_PATTERN,
    RESULT_SCHEMA_VERSION,
)
from src.models import Candidate, CrawlSignals, FeedInfo, LLMSignals

# Classifications that describe a source worth storing. REJECTs are counted in
# `stats.classified` and otherwise dropped (§2.3) — sending two dozen rejected
# candidates per run so .NET can throw them away is bandwidth for nothing.
RETURNED_CLASSIFICATIONS = ("NEWS_SOURCE", "DISCOVERY_SOURCE")


# --- Field-level helpers ----------------------------------------------------

def _null(value: str | None) -> str | None:
    """Empty string → None.

    The dataclasses default to `""`, which is fine internally. On the wire,
    "" and "we could not determine this" are worth distinguishing (§2.4) —
    `language: null` should not land in Postgres as an empty-string column.
    """
    return value if value else None


def _utc(timestamp: str) -> str | None:
    """Normalize an ISO 8601 timestamp to a `Z`-suffixed UTC string.

    `FeedInfo.latest_entry` arrives as `+00:00` from feedparser while the job
    timestamps are already `Z`-suffixed; §2.4 requires one form on the wire.
    A naive timestamp is assumed UTC, matching how the scorer reads it.
    """
    if not timestamp:
        return None
    try:
        when = datetime.fromisoformat(timestamp)
    except ValueError:
        return None
    if when.tzinfo is None:
        when = when.replace(tzinfo=timezone.utc)
    return (
        when.astimezone(timezone.utc)
        .isoformat(timespec="seconds")
        .replace("+00:00", "Z")
    )


def _primary_language(tag: str) -> str | None:
    """Reduce a language tag to its primary subtag: `mk-MK` → `mk`.

    Sites report both forms for the same language — `mk`, `mk-MK`, `en-US` all
    appear in a single Skopje run — and `NewsSource.Language` is one column
    that `WHERE Language = 'mk'` queries. Storing both spellings makes that
    query wrong for half the rows.
    """
    if not tag:
        return None
    return tag.strip().lower().split("-")[0] or None


# --- Name recovery ----------------------------------------------------------
#
# A page title is a masthead as often as it is a tagline, and which one it is
# cannot be decided from a word list: any such list covers only the languages
# someone thought to enumerate, and the pipeline targets 249 countries. So the
# only signal used here is the **domain**, which belongs to no language.

def _alnum(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", text.lower())


def _domain_stem(domain: str) -> str:
    """Strip `www.` and the registry tail off a domain, leaving the brand.

    `novamakedonija.com.mk` → `novamakedonija`, `spiegel.de` → `spiegel`.
    Labels of 3 characters or fewer are dropped as TLDs without needing to be
    listed — that covers every ccTLD plus com/net/org — and at least one label
    always survives, so `prv.mk` stays `prv` rather than becoming empty.
    """
    labels = [l for l in domain.lower().strip().split(".") if l and l != "www"]
    while len(labels) > 1:
        last = labels[-1]
        if len(last) <= 3 or last in DOMAIN_GENERIC_LABELS:
            labels.pop()
        else:
            break
    return _alnum("".join(labels))


def _title_segments(title: str) -> list[str]:
    """Split a title on separators, deduped case-insensitively, order kept.

    Deduping is what reduces "Нова Македонија - Првиот македонски дневен
    весник - Нова Македонија" to two real candidates instead of three.
    """
    segments, seen = [], set()
    for raw in re.split(NAME_SEPARATOR_PATTERN, title):
        segment = raw.strip().strip("-–—|·•").strip()
        key = segment.lower()
        if segment and key not in seen:
            seen.add(key)
            segments.append(segment)
    return segments


def _clean_name(candidate: Candidate, signals: CrawlSignals | None) -> str | None:
    """Recover the site's name from its title, using the domain as the anchor.

    §2.3 orders the sources `page_title` → `site_description` → search-result
    `title`; the first two are only ever empty when the crawl failed.

    Segment selection is language-agnostic by construction:

    1. **The segment the domain vouches for.** News domains are derived from
       mastheads, so "Balkan Insight" matches `balkaninsight.com` and
       "Pravda Северна Македонија" matches `north-macedonia.news-pravda.com`
       even though half of it is Cyrillic. Non-Latin characters normalize away,
       which is why a minimum match length is required — otherwise an empty
       normalization would match every domain.
    2. **Otherwise the shortest segment.** Mastheads are short and taglines are
       long in every language, so this needs no vocabulary. It picks
       "Нова Македонија" over "Првиот македонски дневен весник".

    The residual failure is a title like "Site Name - Home" on a domain that
    matches neither segment, where rule 2 picks "Home". A stopword list would
    fix that for English and Macedonian and leave the other 247 countries
    exactly as they are, so it is left alone: the name is cosmetic, and every
    field .NET keys or joins on is derived from `domain`.
    """
    llm = signals.llm_signals if signals else None
    title = (
        (signals.page_title if signals else "")
        or (llm.site_description if llm else "")
        or candidate.title
    ).strip()
    if not title:
        return None

    segments = _title_segments(title)
    if not segments:
        return _null(title)
    if len(segments) == 1:
        return segments[0]

    stem = _domain_stem(candidate.domain)
    if stem:
        matched = [
            s for s in segments
            if len(_alnum(s)) >= NAME_DOMAIN_MATCH_MIN_CHARS
            and (_alnum(s) in stem or stem in _alnum(s))
        ]
        if matched:
            return min(matched, key=len)

    return min(segments, key=len)


# --- Per-source assembly ----------------------------------------------------

def _categories(signals: CrawlSignals | None) -> list[str]:
    """Nav sections from both extraction passes, lowercased and deduped.

    These become `NewsSource.Categories[]`, which backs the app's
    `'sport' = ANY(Categories)` filter — so the heuristic and LLM spellings of
    the same section ("News" / "news") must collapse to one entry.
    """
    if not signals:
        return []
    llm: LLMSignals | None = signals.llm_signals
    merged, seen = [], set()
    for section in list(signals.sections) + list(llm.sections if llm else []):
        value = section.strip().lower()
        if value and value not in seen:
            seen.add(value)
            merged.append(value)
    return merged


def _feed_payload(feed: FeedInfo) -> dict:
    """One validated feed and what fetching it proved.

    `valid` and `status` are not sent: this only ever runs over feeds where
    `valid` is true, so both are constants on the wire. The failed attempts
    stay in `2_crawl.json` where a human debugging coverage can read them.
    """
    return {
        "url": feed.url,
        "title": _null(feed.title),
        "entry_count": feed.entry_count,
        "latest_entry": _utc(feed.latest_entry),
        "has_full_content": feed.has_full_content,
        "language": _primary_language(feed.language),
        "external_link_ratio": feed.external_link_ratio,
        "distinct_sources": feed.distinct_sources,
    }


def _source_payload(candidate: Candidate, source_facts_refreshed: bool) -> dict:
    signals = candidate.signals
    llm = signals.llm_signals if signals else None
    classification = candidate.classification
    scoring = candidate.scoring

    language = _primary_language(
        (signals.language if signals else "") or (llm.language if llm else "")
    )

    return {
        # `homepage_url`, never `url`: the latter is the raw search hit, which
        # is routinely a deep page like /where/north-macedonia.
        "domain": candidate.domain,
        "name": _clean_name(candidate, signals),
        "url": _null(candidate.homepage_url or candidate.url),
        "source_facts_refreshed": source_facts_refreshed,
        "language": language,
        "classification": classification.classification if classification else None,
        "confidence": classification.confidence if classification else 0.0,
        "categories": _categories(signals),
        "feeds": [
            _feed_payload(f) for f in (signals.feeds if signals else []) if f.valid
        ],
        "relevance": {
            "score": scoring.score if scoring else 0.0,
            "polling_tier": scoring.polling_tier if scoring else None,
            "search_occurrences": candidate.search_occurrences,
            "matched_queries": list(candidate.queries),
        },
        "evidence": {
            "reason": _null(classification.reason) if classification else None,
            "article_like_paths": signals.article_like_paths if signals else 0,
            "has_date_patterns": signals.has_date_patterns if signals else False,
            "author_count": len(signals.authors) if signals else 0,
        },
    }


# --- Entry point ------------------------------------------------------------

def _location_payload(location: dict) -> dict:
    return {
        "iso2": location.get("iso2") or None,
        "iso3": location.get("iso3") or None,
        "country_name": location.get("country_name") or None,
        "city": location.get("city") or None,
        "city_local_name": location.get("city_local_name") or None,
        "city_id": location.get("city_id") or None,
    }


def build_revalidation_payload(
    pairs: list[tuple[object, FeedInfo]],
) -> dict:
    """The §4.4 response: what re-fetching each stored feed proved.

    Built here rather than in the endpoint so revalidation and discovery share
    one normalization. Both write the same `NewsSourceFeed` columns, and if the
    two paths disagreed — `mk-MK` from one and `mk` from the other, `+00:00`
    against `Z` — every nightly revalidation would rewrite rows discovery had
    just written, and the diff would look like real change.

    Unlike `_feed_payload`'s use in `build_result_payload`, `valid` and `status`
    *are* sent. There they are constants, because that path filters to valid
    feeds first; here they are the entire question being asked.

    `source_feed_id` is echoed untouched and is the only safe join key: a valid
    feed reports its post-redirect URL, so the URL .NET sent may not be the URL
    it gets back — which is itself worth storing, since the redirect target is
    the canonical thing to poll.
    """
    return {
        "results": [
            {
                "source_feed_id": source_feed_id,
                "valid": feed.valid,
                "status": _null(feed.status),
                **_feed_payload(feed),
            }
            for source_feed_id, feed in pairs
        ]
    }


def build_result_payload(
    manifest: dict,
    candidates: list[Candidate],
    known_domains: set[str] | None = None,
) -> dict:
    """Build the §2 result payload from what `run_pipeline()` returns.

    Takes the manifest rather than the loose `(job_id, location, started_at)`
    the plan sketches, because all three already live in it and passing them
    alongside invites the two copies to drift.

    `known_domains` is the §4.1 set whose source facts .NET already holds. It
    arrives on the job request and is threaded through here; until Phase 2
    wires it up, callers pass nothing and every source reports
    `source_facts_refreshed: true`.

    A run that did not complete carries no sources — `status` is binding and
    `warnings` advisory (§2.2), so a degraded run must not hand .NET rows to
    upsert on the strength of a warning it is allowed to ignore.
    """
    known = known_domains or set()
    completed = manifest.get("status") == "completed"

    sources: list[dict] = []
    if completed:
        for candidate in candidates:
            classification = candidate.classification
            if not classification or classification.classification not in RETURNED_CLASSIFICATIONS:
                continue
            sources.append(_source_payload(candidate, candidate.domain not in known))
        # Highest relevance first: the order .NET would otherwise sort into to
        # decide what its poller reaches for.
        sources.sort(key=lambda s: s["relevance"]["score"], reverse=True)

    stats = dict(manifest.get("stats") or {})
    stats["returned"] = len(sources)

    return {
        "schema_version": RESULT_SCHEMA_VERSION,
        "job_id": manifest.get("job_id"),
        "status": manifest.get("status"),
        "location": _location_payload(manifest.get("location") or {}),
        "started_at": manifest.get("started_at"),
        "finished_at": manifest.get("finished_at"),
        "stats": stats,
        "warnings": list(manifest.get("warnings") or []),
        "error": manifest.get("error"),
        "sources": sources,
    }
