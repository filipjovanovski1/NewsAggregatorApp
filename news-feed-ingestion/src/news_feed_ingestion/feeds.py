from __future__ import annotations

import calendar
import html
import re
from dataclasses import dataclass, field
from datetime import UTC, datetime
from time import struct_time
from typing import Any

import feedparser

from .urls import normalize_url


class FeedParseError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class FeedEntry:
    external_id: str
    url: str
    title: str
    summary: str | None
    feed_content: str | None
    author: str | None
    published_at: datetime
    modified_at: datetime | None
    categories: list[str] = field(default_factory=list)
    image_url: str | None = None


def parse_feed(content: bytes, *, max_entries: int, fallback_time: datetime) -> list[FeedEntry]:
    parsed = feedparser.parse(content)
    if parsed.bozo and not parsed.entries:
        detail = str(getattr(parsed, "bozo_exception", "malformed feed"))
        raise FeedParseError(detail)

    entries: list[FeedEntry] = []
    for item in parsed.entries[:max_entries]:
        raw_url = _text(item.get("link"))
        title = _clean_text(_text(item.get("title")))
        if not raw_url or not title:
            continue
        try:
            url = normalize_url(raw_url)
        except ValueError:
            continue

        summary_html = _text(item.get("summary") or item.get("description"))
        content_html = _entry_content(item)
        external_id = _text(item.get("id") or item.get("guid")) or url
        published = _parsed_time(item.get("published_parsed")) or _parsed_time(
            item.get("updated_parsed")
        )
        modified = _parsed_time(item.get("updated_parsed"))

        entries.append(
            FeedEntry(
                external_id=external_id.strip(),
                url=url,
                title=title,
                summary=_clean_html(summary_html),
                feed_content=_clean_html(content_html),
                author=_clean_text(_text(item.get("author"))),
                published_at=published or fallback_time,
                modified_at=modified if modified != published else None,
                categories=_categories(item),
                image_url=_image_url(item),
            )
        )
    return entries


def _entry_content(item: Any) -> str | None:
    values = item.get("content") or []
    if values and isinstance(values, list):
        return _text(values[0].get("value"))
    return None


def _categories(item: Any) -> list[str]:
    result: list[str] = []
    for tag in item.get("tags") or []:
        value = _clean_text(_text(tag.get("term")))
        if value and value.lower() not in result:
            result.append(value.lower())
    return result


def _image_url(item: Any) -> str | None:
    for key in ("media_content", "media_thumbnail"):
        values = item.get(key) or []
        for value in values:
            if value.get("url"):
                return _text(value["url"])
    for enclosure in item.get("enclosures") or []:
        if str(enclosure.get("type", "")).startswith("image/") and enclosure.get("href"):
            return _text(enclosure["href"])
    return None


def _parsed_time(value: struct_time | None) -> datetime | None:
    if value is None:
        return None
    return datetime.fromtimestamp(calendar.timegm(value), tz=UTC)


def _text(value: Any) -> str | None:
    if value is None:
        return None
    result = str(value).strip()
    return result or None


def _clean_text(value: str | None) -> str | None:
    if not value:
        return None
    return " ".join(html.unescape(value).split()) or None


def _clean_html(value: str | None) -> str | None:
    if not value:
        return None
    without_tags = re.sub(r"<[^>]+>", " ", value)
    return _clean_text(without_tags)
