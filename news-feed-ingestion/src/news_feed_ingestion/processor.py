from __future__ import annotations

import asyncio
import logging
from datetime import UTC, datetime
from time import monotonic
from urllib.parse import urljoin, urlsplit

import httpx

from .backend import BackendClient
from .config import Settings
from .extraction import ExtractedArticle, extract_article, extractor_version
from .feeds import FeedEntry, FeedParseError, parse_feed
from .http import SafeHttpFetcher
from .models import (
    ArticlePayload,
    CompleteRequest,
    FailureRequest,
    FailureStage,
    FeedClaim,
    FeedPollMetadata,
)
from .urls import content_hash, normalize_url, same_site

logger = logging.getLogger(__name__)


class FeedProcessingError(RuntimeError):
    def __init__(self, stage: FailureStage, cause: Exception) -> None:
        self.stage = stage
        self.cause = cause
        super().__init__(str(cause))


class FeedProcessor:
    def __init__(
        self,
        *,
        settings: Settings,
        backend: BackendClient,
        fetcher: SafeHttpFetcher,
    ) -> None:
        self._settings = settings
        self._backend = backend
        self._fetcher = fetcher
        self._article_slots = asyncio.Semaphore(settings.max_concurrent_articles)

    async def process(self, claim: FeedClaim) -> None:
        started = monotonic()
        try:
            completion = await self._poll(claim)
            try:
                await self._backend.complete(claim.lease_id, completion)
            except Exception as exc:
                raise FeedProcessingError("backend_delivery", exc) from exc

            extracted = sum(item.extraction_status == "extracted" for item in completion.articles)
            logger.info(
                "feed poll completed",
                extra={
                    "worker_id": self._settings.worker_id,
                    "lease_id": str(claim.lease_id),
                    "lease_version": claim.lease_version,
                    "feed_id": str(claim.feed_id),
                    "source_id": str(claim.source_id),
                    "source_domain": claim.source_domain,
                    "http_status": completion.feed.http_status,
                    "article_count": len(completion.articles),
                    "extracted_count": extracted,
                    "rss_only_count": len(completion.articles) - extracted,
                    "elapsed_seconds": round(monotonic() - started, 3),
                },
            )
        except FeedProcessingError as exc:
            await self._report_failure(claim, exc)
            logger.warning(
                "feed poll failed",
                exc_info=exc.cause,
                extra={
                    "worker_id": self._settings.worker_id,
                    "lease_id": str(claim.lease_id),
                    "lease_version": claim.lease_version,
                    "feed_id": str(claim.feed_id),
                    "source_domain": claim.source_domain,
                    "error_stage": exc.stage,
                    "error_type": type(exc.cause).__name__,
                    "elapsed_seconds": round(monotonic() - started, 3),
                },
            )

    async def _poll(self, claim: FeedClaim) -> CompleteRequest:
        polled_at = datetime.now(UTC)
        try:
            response = await self._fetcher.fetch_feed(
                claim.feed_url,
                etag=claim.etag,
                last_modified=claim.last_modified,
                timeout_seconds=self._settings.feed_timeout_seconds,
                max_bytes=self._settings.feed_max_bytes,
            )
        except Exception as exc:
            raise FeedProcessingError("feed_fetch", exc) from exc

        metadata = FeedPollMetadata(
            requested_url=claim.feed_url,
            final_url=response.final_url,
            http_status=response.status_code,
            etag=response.headers.get("etag") or claim.etag,
            last_modified=response.headers.get("last-modified") or claim.last_modified,
            polled_at=polled_at,
        )
        if response.status_code == 304:
            return CompleteRequest(lease_version=claim.lease_version, feed=metadata, articles=[])

        try:
            entries = parse_feed(
                response.content,
                max_entries=self._settings.max_articles_per_feed,
                fallback_time=polled_at,
            )
        except FeedParseError as exc:
            raise FeedProcessingError("feed_parse", exc) from exc

        articles = await asyncio.gather(*(self._process_entry(entry, claim) for entry in entries))
        return CompleteRequest(
            lease_version=claim.lease_version,
            feed=metadata,
            articles=list(articles),
        )

    async def _process_entry(self, entry: FeedEntry, claim: FeedClaim) -> ArticlePayload:
        extracted = ExtractedArticle()
        extraction_attempted = False
        try:
            async with self._article_slots:
                response = await self._fetcher.fetch_article(
                    entry.url,
                    timeout_seconds=self._settings.article_timeout_seconds,
                    max_bytes=self._settings.article_max_bytes,
                )
                extraction_attempted = True
                extracted = await extract_article(
                    response.content,
                    url=response.final_url,
                    favor_precision=self._settings.trafilatura_favor_precision,
                )
                final_url = response.final_url
        except Exception as exc:
            final_url = entry.url
            logger.info(
                "article extraction fell back to RSS metadata",
                extra={
                    "feed_id": str(claim.feed_id),
                    "article_host": urlsplit(entry.url).hostname,
                    "error_type": type(exc).__name__,
                },
            )

        canonical_url = _canonical_url(extracted.canonical_url, final_url)
        content = extracted.text or entry.feed_content
        description = entry.summary or _excerpt(content)
        categories = list(dict.fromkeys(entry.categories + extracted.categories))
        status = "extracted" if extracted.text else "rss_only"
        image_url = _optional_http_url(extracted.image_url or entry.image_url, base_url=final_url)

        return ArticlePayload(
            external_entry_id=entry.external_id,
            entry_url=entry.url,
            canonical_url=canonical_url,
            title=entry.title or extracted.title or canonical_url,
            description=description,
            content_text=content,
            author=extracted.author or entry.author,
            language=extracted.language or claim.language,
            image_url=image_url,
            published_at=entry.published_at or extracted.published_at or datetime.now(UTC),
            modified_at=entry.modified_at,
            categories=categories,
            content_hash=content_hash(content),
            extraction_status=status,
            extractor="trafilatura" if extraction_attempted else None,
            extractor_version=extractor_version() if extraction_attempted else None,
        )

    async def _report_failure(self, claim: FeedClaim, error: FeedProcessingError) -> None:
        status = (
            error.cause.response.status_code
            if isinstance(error.cause, httpx.HTTPStatusError)
            else None
        )
        request = FailureRequest(
            lease_version=claim.lease_version,
            failed_at=datetime.now(UTC),
            stage=error.stage,
            error_type=type(error.cause).__name__,
            message=str(error.cause)[:1000],
            http_status=status,
        )
        try:
            await self._backend.fail(claim.lease_id, request)
        except Exception:
            logger.exception(
                "could not report feed failure; lease will expire",
                extra={"lease_id": str(claim.lease_id), "feed_id": str(claim.feed_id)},
            )


def _canonical_url(candidate: str | None, final_url: str) -> str:
    try:
        if candidate and same_site(candidate, final_url):
            return normalize_url(candidate)
    except ValueError:
        pass
    return normalize_url(final_url)


def _optional_http_url(value: str | None, *, base_url: str) -> str | None:
    if not value:
        return None
    try:
        return normalize_url(urljoin(base_url, value))
    except ValueError:
        return None


def _excerpt(content: str | None, length: int = 280) -> str | None:
    if not content:
        return None
    text = " ".join(content.split())
    return text if len(text) <= length else text[: length - 1].rstrip() + "…"
