"""Tests for the §2 result payload builder.

`src/output.py` is the only module in the pipeline that runs without a browser,
an Ollama, or a network, which is why it is the one that gets tested. What is
covered here is the wire contract: the rules .NET deserializes against and that
would otherwise break silently, since every field degrades to a default rather
than an error.
"""

import pytest

from src.models import (
    Candidate,
    ClassificationResult,
    CrawlSignals,
    FeedInfo,
    LLMSignals,
    ScoringResult,
)
from src.output import (
    _clean_name,
    _domain_stem,
    build_result_payload,
    build_revalidation_payload,
)


def manifest(**overrides) -> dict:
    base = {
        "job_id": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "status": "completed",
        "location": {
            "iso2": "MK",
            "iso3": "MKD",
            "country_name": "North Macedonia",
            "city": "Skopje",
            "city_local_name": "Скопје",
            "city_id": "9c5b94b1-35ad-49bb-b118-8e8fc24abf80",
        },
        "started_at": "2026-08-21T10:02:11Z",
        "finished_at": "2026-08-21T10:09:47Z",
        "stats": {"discovered": 41},
        "warnings": [],
        "error": None,
    }
    return {**base, **overrides}


def source(domain="mkd.mk", classification="NEWS_SOURCE", **kwargs) -> Candidate:
    return Candidate(
        domain=domain,
        homepage_url=f"https://{domain}",
        classification=ClassificationResult(classification=classification, confidence=0.9),
        scoring=ScoringResult(score=87.4, polling_tier="high"),
        **kwargs,
    )


# --- Name recovery ----------------------------------------------------------
#
# The rules are language-agnostic by construction: the domain is the anchor and
# there is no vocabulary anywhere. These cases are drawn from a real MK/Skopje
# run and deliberately mix scripts — a regression that reintroduced an
# English-only rule would pass on the Latin rows alone.

@pytest.mark.parametrize("domain,title,expected", [
    # The domain vouches for a segment, in either direction.
    ("balkaninsight.com",
     "Covering politics, society and business in Southern and Eastern Europe | Balkan Insight",
     "Balkan Insight"),
    ("apnews.com",
     "Associated Press News: Breaking News, Latest Headlines and Videos  | AP News",
     "AP News"),
    ("meta.mk", "Meta.mk – Новинска Агенција", "Meta.mk"),
    # Mixed script: "Pravda" carries the match, the Cyrillic half normalizes away.
    ("north-macedonia.news-pravda.com",
     "Pravda Северна Македонија - Вести Од Северна Македонија",
     "Pravda Северна Македонија"),
    # No Latin overlap at all — the shortest segment wins, which needs no
    # vocabulary and is what makes this work outside the languages we can read.
    ("novamakedonija.com.mk",
     "Нова Македонија - Првиот македонски дневен весник - Нова Македонија",
     "Нова Македонија"),
    ("prv.mk", "ПРВ.мк - Најнови вести од Македонија и светот", "ПРВ.мк"),
    # A short segment must not win when the domain names a longer one.
    ("spiegel.de", "News | Der Spiegel", "Der Spiegel"),
    # Nothing to split.
    ("mrt.com.mk", "МРТ", "МРТ"),
    # Separators that must not fire: a clock time is not a separator (this
    # resolved to "30"), and a hyphenated word is not two segments.
    ("tv-nova.example", "Vesti 19:30", "Vesti 19:30"),
    ("example.com", "News at 6:30 | Channel 5", "Channel 5"),
    ("north-macedonia.example", "north-macedonia today", "north-macedonia today"),
])
def test_clean_name(domain, title, expected):
    candidate = Candidate(domain=domain)
    assert _clean_name(candidate, CrawlSignals(page_title=title)) == expected


@pytest.mark.parametrize("domain,stem", [
    ("novamakedonija.com.mk", "novamakedonija"),
    ("www.balkaninsight.com", "balkaninsight"),
    ("spiegel.de", "spiegel"),
    ("prv.mk", "prv"),          # never strips down to nothing
    ("north-macedonia.news-pravda.com", "northmacedonianewspravda"),
])
def test_domain_stem(domain, stem):
    assert _domain_stem(domain) == stem


def test_name_falls_back_through_the_documented_order():
    """page_title → site_description → search title (§2.3)."""
    candidate = Candidate(domain="x.mk", title="from search")
    signals = CrawlSignals(llm_signals=LLMSignals(site_description="from llm"))
    assert _clean_name(candidate, signals) == "from llm"
    assert _clean_name(candidate, CrawlSignals()) == "from search"
    assert _clean_name(Candidate(domain="x.mk"), None) is None


# --- The contract -----------------------------------------------------------

def test_rejects_are_counted_but_not_sent():
    candidates = [
        source("a.mk"),
        source("b.mk", classification="DISCOVERY_SOURCE"),
        source("c.mk", classification="REJECT"),
        Candidate(domain="d.mk"),  # never classified at all
    ]
    payload = build_result_payload(manifest(), candidates)
    assert [s["domain"] for s in payload["sources"]] == ["a.mk", "b.mk"]
    assert payload["stats"]["returned"] == 2


def test_only_valid_feeds_are_sent():
    candidate = source(signals=CrawlSignals(feeds=[
        FeedInfo(url="https://mkd.mk/feed", valid=True, status="ok", entry_count=25),
        FeedInfo(url="https://mkd.mk/rss", valid=False, status="http_403"),
    ]))
    feeds = build_result_payload(manifest(), [candidate])["sources"][0]["feeds"]
    assert [f["url"] for f in feeds] == ["https://mkd.mk/feed"]
    # `valid`/`status` are constants once filtered, so they are not on the wire.
    assert "valid" not in feeds[0] and "status" not in feeds[0]


def test_timestamps_are_z_suffixed_utc():
    candidate = source(signals=CrawlSignals(feeds=[
        FeedInfo(url="u", valid=True, latest_entry="2026-08-21T12:25:10+00:00"),
    ]))
    payload = build_result_payload(manifest(), [candidate])
    assert payload["sources"][0]["feeds"][0]["latest_entry"] == "2026-08-21T12:25:10Z"


def test_language_is_reduced_to_its_primary_subtag():
    """`mk-MK` and `mk` must not both land in one queryable column."""
    candidate = source(signals=CrawlSignals(
        language="mk-MK",
        feeds=[FeedInfo(url="u", valid=True, language="en-US")],
    ))
    payload = build_result_payload(manifest(), [candidate])["sources"][0]
    assert payload["language"] == "mk"
    assert payload["feeds"][0]["language"] == "en"


def test_language_falls_back_to_the_llm_pass():
    candidate = source(signals=CrawlSignals(llm_signals=LLMSignals(language="sq")))
    assert build_result_payload(manifest(), [candidate])["sources"][0]["language"] == "sq"


def test_absent_strings_are_null_not_empty():
    payload = build_result_payload(
        manifest(location={"iso2": "MK"}), [Candidate(domain="x.mk")]
    )
    assert payload["location"]["city"] is None
    assert payload["location"]["iso3"] is None


def test_categories_merge_both_passes_lowercased_and_deduped():
    candidate = source(signals=CrawlSignals(
        sections=["Politika", "Sport"],
        llm_signals=LLMSignals(sections=["sport", "Ekonomija"]),
    ))
    categories = build_result_payload(manifest(), [candidate])["sources"][0]["categories"]
    assert categories == ["politika", "sport", "ekonomija"]


def test_run_facts_stay_nested_under_relevance():
    """§1: flattening these means the next city to find a shared regional
    outlet silently overwrites this city's score."""
    candidate = source(queries=["Скопје вести"], search_occurrences=6)
    payload = build_result_payload(manifest(), [candidate])["sources"][0]
    assert payload["relevance"] == {
        "score": 87.4,
        "polling_tier": "high",
        "search_occurrences": 6,
        "matched_queries": ["Скопје вести"],
    }
    assert "score" not in payload and "polling_tier" not in payload


def test_sources_are_ordered_by_relevance():
    low = source("low.mk")
    low.scoring = ScoringResult(score=12.0, polling_tier="backup")
    payload = build_result_payload(manifest(), [low, source("high.mk")])
    assert [s["domain"] for s in payload["sources"]] == ["high.mk", "low.mk"]


def test_known_domains_mark_source_facts_stale():
    candidates = [source("known.mk"), source("new.mk")]
    payload = build_result_payload(manifest(), candidates, known_domains={"known.mk"})
    refreshed = {s["domain"]: s["source_facts_refreshed"] for s in payload["sources"]}
    assert refreshed == {"known.mk": False, "new.mk": True}
    # Skipped domains must still be sent, or §3's staleness sweep deletes
    # their scope rows.
    assert payload["stats"]["returned"] == 2


def test_a_failed_run_carries_no_sources():
    """§2.2: `status` is binding, `warnings` advisory. A degraded run must not
    hand .NET rows to upsert on the strength of a warning it may ignore."""
    error = {"stage": "classify", "type": "StageFailure", "message": "unreachable"}
    payload = build_result_payload(
        manifest(status="failed", error=error), [source(), source("b.mk")]
    )
    assert payload["sources"] == []
    assert payload["stats"]["returned"] == 0
    assert payload["error"] == error


def test_zero_source_success_is_distinct_from_failure():
    payload = build_result_payload(manifest(), [source(classification="REJECT")])
    assert payload["status"] == "completed"
    assert payload["sources"] == [] and payload["error"] is None


def test_envelope_passes_the_manifest_through():
    payload = build_result_payload(manifest(warnings=["classify: 2/41 failed"]), [])
    assert payload["schema_version"] == 1
    assert payload["job_id"] == "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
    assert payload["started_at"] == "2026-08-21T10:02:11Z"
    assert payload["finished_at"] == "2026-08-21T10:09:47Z"
    assert payload["warnings"] == ["classify: 2/41 failed"]
    assert payload["location"]["city_id"] == "9c5b94b1-35ad-49bb-b118-8e8fc24abf80"


# --- §4.4 revalidation response ---------------------------------------------
#
# The join key is the whole risk here. .NET matches results back on
# `source_feed_id`, so anything that reorders, drops, or collapses entries
# corrupts rows rather than failing — a feed's liveness written onto a
# different feed's row.

def test_revalidation_echoes_the_id_untouched():
    payload = build_revalidation_payload([(42, FeedInfo(url="https://mkd.mk/feed", valid=True))])
    assert payload["results"][0]["source_feed_id"] == 42


def test_revalidation_preserves_order_and_arity():
    pairs = [(i, FeedInfo(url=f"https://s{i}.mk/feed")) for i in range(5)]
    results = build_revalidation_payload(pairs)["results"]
    assert [r["source_feed_id"] for r in results] == [0, 1, 2, 3, 4]


def test_revalidation_keeps_identical_urls_apart():
    """Two rows may legitimately store the same URL; neither may be collapsed."""
    pairs = [(1, FeedInfo(url="https://mkd.mk/feed")), (2, FeedInfo(url="https://mkd.mk/feed"))]
    assert len(build_revalidation_payload(pairs)["results"]) == 2


def test_revalidation_sends_valid_and_status():
    """Dropped from the discovery payload as constants; here they are the answer."""
    dead = build_revalidation_payload([(1, FeedInfo(url="x", status="http_404"))])["results"][0]
    assert dead["valid"] is False and dead["status"] == "http_404"


def test_revalidation_reports_the_post_redirect_url():
    """The canonical thing to poll, and why position rather than URL is the key."""
    feed = FeedInfo(url="https://theguardian.com/uk/rss", valid=True, status="ok")
    assert build_revalidation_payload([(7, feed)])["results"][0]["url"] == (
        "https://theguardian.com/uk/rss"
    )


def test_revalidation_normalizes_exactly_like_discovery():
    """Both paths write the same columns; divergence would churn rows nightly."""
    feed = FeedInfo(
        url="https://mkd.mk/feed",
        valid=True,
        status="ok",
        language="mk-MK",
        latest_entry="2026-08-21T08:40:00+00:00",
        entry_count=25,
    )
    revalidated = build_revalidation_payload([(1, feed)])["results"][0]
    candidate = source(signals=CrawlSignals(crawl_success=True, feeds=[feed]))
    discovered = build_result_payload(manifest(), [candidate])["sources"][0]["feeds"][0]

    assert revalidated["language"] == discovered["language"] == "mk"
    assert revalidated["latest_entry"] == discovered["latest_entry"] == "2026-08-21T08:40:00Z"


def test_revalidation_carries_the_aggregator_discriminators():
    feed = FeedInfo(url="x", valid=True, status="ok", external_link_ratio=1.0, distinct_sources=27)
    row = build_revalidation_payload([(1, feed)])["results"][0]
    assert row["external_link_ratio"] == 1.0 and row["distinct_sources"] == 27
