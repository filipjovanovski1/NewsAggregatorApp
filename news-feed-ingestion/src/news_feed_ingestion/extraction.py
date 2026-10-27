from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

import trafilatura
from lxml import html as lxml_html
from trafilatura import bare_extraction


@dataclass(frozen=True, slots=True)
class ExtractedArticle:
    title: str | None = None
    text: str | None = None
    author: str | None = None
    language: str | None = None
    image_url: str | None = None
    canonical_url: str | None = None
    published_at: datetime | None = None
    categories: list[str] = field(default_factory=list)


async def extract_article(
    html: bytes,
    *,
    url: str,
    favor_precision: bool,
) -> ExtractedArticle:
    return await asyncio.to_thread(
        _extract_sync,
        html.decode("utf-8", errors="replace"),
        url,
        favor_precision,
    )


def extractor_version() -> str:
    return getattr(trafilatura, "__version__", "unknown")


def _extract_sync(html_text: str, url: str, favor_precision: bool) -> ExtractedArticle:
    metadata = _html_metadata(html_text)
    document = bare_extraction(
        html_text,
        url=url,
        include_comments=False,
        include_tables=False,
        favor_precision=favor_precision,
    )
    if document is None:
        return metadata

    return ExtractedArticle(
        title=_clean(getattr(document, "title", None)) or metadata.title,
        text=_clean(getattr(document, "text", None)),
        author=_clean(getattr(document, "author", None)) or metadata.author,
        language=_clean(getattr(document, "language", None)) or metadata.language,
        image_url=_clean(getattr(document, "image", None)) or metadata.image_url,
        canonical_url=_clean(getattr(document, "url", None)) or metadata.canonical_url,
        published_at=_date(getattr(document, "date", None)),
        categories=list(
            dict.fromkeys(
                _string_list(getattr(document, "categories", None)) + metadata.categories
            )
        ),
    )


def _html_metadata(html_text: str) -> ExtractedArticle:
    try:
        root = lxml_html.fromstring(html_text)
    except (TypeError, ValueError):
        return ExtractedArticle()

    def first(xpath: str) -> str | None:
        values = root.xpath(xpath)
        return _clean(values[0]) if values else None

    tags = [
        _clean(value)
        for value in root.xpath(
            "//meta[@property='article:tag']/@content | //meta[@name='news_keywords']/@content"
        )
    ]
    categories = [
        item.strip().lower()
        for value in tags
        if value
        for item in value.split(",")
        if item.strip()
    ]
    return ExtractedArticle(
        title=first("//meta[@property='og:title']/@content") or first("//title/text()"),
        author=first("//meta[@name='author']/@content"),
        language=first("/html/@lang"),
        image_url=first("//meta[@property='og:image']/@content"),
        canonical_url=first(
            "//link[contains(concat(' ', normalize-space(@rel), ' '), ' canonical ')]/@href"
        ),
        categories=list(dict.fromkeys(categories)),
    )


def _clean(value: Any) -> str | None:
    if value is None:
        return None
    result = " ".join(str(value).split())
    return result or None


def _date(value: Any) -> datetime | None:
    text = _clean(value)
    if not text:
        return None
    try:
        return datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        return None


def _string_list(value: Any) -> list[str]:
    if not value:
        return []
    values = value if isinstance(value, (list, tuple, set)) else str(value).split(";")
    return list(dict.fromkeys(item.strip().lower() for item in values if str(item).strip()))
