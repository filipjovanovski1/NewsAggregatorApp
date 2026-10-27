from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4

import httpx
import pytest

from news_feed_ingestion.config import Settings
from news_feed_ingestion.http import FetchedResponse
from news_feed_ingestion.models import CompleteRequest, FailureRequest, FeedClaim
from news_feed_ingestion.processor import FeedProcessor

FIXTURES = Path(__file__).parent / "fixtures"


class FakeBackend:
    def __init__(self) -> None:
        self.completed: list[CompleteRequest] = []
        self.failures: list[FailureRequest] = []

    async def complete(self, lease_id: object, request: CompleteRequest) -> None:
        self.completed.append(request)

    async def fail(self, lease_id: object, request: FailureRequest) -> None:
        self.failures.append(request)


class FakeFetcher:
    def __init__(self, article_error: Exception | None = None) -> None:
        self.article_error = article_error

    async def fetch_feed(self, url: str, **kwargs: object) -> FetchedResponse:
        return FetchedResponse(
            requested_url=url,
            final_url="https://news.example.com/feed.xml",
            status_code=200,
            headers=httpx.Headers({"etag": '"v2"'}),
            content=(FIXTURES / "feed.xml").read_bytes(),
        )

    async def fetch_article(self, url: str, **kwargs: object) -> FetchedResponse:
        if self.article_error:
            raise self.article_error
        return FetchedResponse(
            requested_url=url,
            final_url="https://news.example.com/story/123",
            status_code=200,
            headers=httpx.Headers({"content-type": "text/html"}),
            content=(FIXTURES / "article.html").read_bytes(),
        )


def settings() -> Settings:
    return Settings(
        backend_base_url="https://backend.example",
        backend_ingestion_token="secret",
    )


def claim() -> FeedClaim:
    return FeedClaim(
        lease_id=uuid4(),
        lease_version=7,
        lease_expires_at=datetime.now(UTC) + timedelta(minutes=5),
        feed_id=uuid4(),
        feed_url="https://news.example.com/feed.xml",
        source_id=uuid4(),
        source_domain="news.example.com",
        source_name="Example News",
        language="mk",
    )


@pytest.mark.asyncio
async def test_processes_feed_extracts_article_and_completes() -> None:
    backend = FakeBackend()
    processor = FeedProcessor(
        settings=settings(),
        backend=backend,  # type: ignore[arg-type]
        fetcher=FakeFetcher(),  # type: ignore[arg-type]
    )

    await processor.process(claim())

    assert backend.failures == []
    assert len(backend.completed) == 1
    article = backend.completed[0].articles[0]
    assert article.extraction_status == "extracted"
    assert article.canonical_url == "https://news.example.com/story/123"
    assert article.author == "Page Author"
    assert article.language == "mk"
    assert article.content_hash is not None


@pytest.mark.asyncio
async def test_article_failure_is_delivered_as_rss_only() -> None:
    backend = FakeBackend()
    processor = FeedProcessor(
        settings=settings(),
        backend=backend,  # type: ignore[arg-type]
        fetcher=FakeFetcher(article_error=TimeoutError("publisher timed out")),  # type: ignore[arg-type]
    )

    await processor.process(claim())

    assert backend.failures == []
    article = backend.completed[0].articles[0]
    assert article.extraction_status == "rss_only"
    assert article.description == "Краток опис на веста."
