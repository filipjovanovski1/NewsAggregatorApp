import httpx
import pytest

from news_feed_ingestion.http import ResponseTooLargeError, SafeHttpFetcher


async def allow_test_url(value: str) -> str:
    return value


@pytest.mark.asyncio
async def test_conditional_feed_request_accepts_not_modified() -> None:
    async def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["if-none-match"] == '"v1"'
        assert request.headers["if-modified-since"] == "yesterday"
        return httpx.Response(304, headers={"etag": '"v1"'})

    http = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    fetcher = SafeHttpFetcher(
        user_agent="test",
        max_redirects=2,
        max_requests_per_host=1,
        client=http,
        url_validator=allow_test_url,
    )

    response = await fetcher.fetch_feed(
        "https://example.com/feed.xml",
        etag='"v1"',
        last_modified="yesterday",
        timeout_seconds=1,
        max_bytes=100,
    )

    assert response.status_code == 304
    assert response.content == b""
    await http.aclose()


@pytest.mark.asyncio
async def test_decompressed_body_size_is_limited() -> None:
    async def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, headers={"content-type": "text/html"}, content=b"x" * 11)

    http = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    fetcher = SafeHttpFetcher(
        user_agent="test",
        max_redirects=2,
        max_requests_per_host=1,
        client=http,
        url_validator=allow_test_url,
    )

    with pytest.raises(ResponseTooLargeError):
        await fetcher.fetch_article(
            "https://example.com/story",
            timeout_seconds=1,
            max_bytes=10,
        )
    await http.aclose()
