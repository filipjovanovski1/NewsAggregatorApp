from __future__ import annotations

import asyncio
from collections import defaultdict
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from urllib.parse import urljoin, urlsplit

import httpx

from .urls import validate_public_url


class ResponseTooLargeError(ValueError):
    pass


class UnexpectedContentTypeError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class FetchedResponse:
    requested_url: str
    final_url: str
    status_code: int
    headers: httpx.Headers
    content: bytes


class SafeHttpFetcher:
    """HTTP client with bounded bodies, manual redirects, and public-address checks."""

    FEED_CONTENT_TYPES = {
        "application/atom+xml",
        "application/json",
        "application/octet-stream",
        "application/rdf+xml",
        "application/rss+xml",
        "application/xml",
        "text/plain",
        "text/xml",
    }
    ARTICLE_CONTENT_TYPES = {"application/xhtml+xml", "text/html"}

    def __init__(
        self,
        *,
        user_agent: str,
        max_redirects: int,
        max_requests_per_host: int,
        client: httpx.AsyncClient | None = None,
        url_validator: Callable[[str], Awaitable[str]] = validate_public_url,
    ) -> None:
        self._max_redirects = max_redirects
        self._max_requests_per_host = max_requests_per_host
        self._host_locks: defaultdict[str, asyncio.Semaphore] = defaultdict(
            lambda: asyncio.Semaphore(self._max_requests_per_host)
        )
        self._owns_client = client is None
        self._url_validator = url_validator
        self._client = client or httpx.AsyncClient(
            follow_redirects=False,
            headers={"User-Agent": user_agent},
        )

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def fetch_feed(
        self,
        url: str,
        *,
        etag: str | None,
        last_modified: str | None,
        timeout_seconds: float,
        max_bytes: int,
    ) -> FetchedResponse:
        headers = {
            "Accept": (
                "application/rss+xml, application/atom+xml, application/xml, "
                "text/xml;q=0.9, */*;q=0.1"
            )
        }
        if etag:
            headers["If-None-Match"] = etag
        if last_modified:
            headers["If-Modified-Since"] = last_modified
        return await self._fetch(
            url,
            headers=headers,
            timeout_seconds=timeout_seconds,
            max_bytes=max_bytes,
            accepted_types=self.FEED_CONTENT_TYPES,
            allow_not_modified=True,
        )

    async def fetch_article(
        self,
        url: str,
        *,
        timeout_seconds: float,
        max_bytes: int,
    ) -> FetchedResponse:
        return await self._fetch(
            url,
            headers={"Accept": "text/html, application/xhtml+xml;q=0.9"},
            timeout_seconds=timeout_seconds,
            max_bytes=max_bytes,
            accepted_types=self.ARTICLE_CONTENT_TYPES,
            allow_not_modified=False,
        )

    async def _fetch(
        self,
        url: str,
        *,
        headers: dict[str, str],
        timeout_seconds: float,
        max_bytes: int,
        accepted_types: set[str],
        allow_not_modified: bool,
    ) -> FetchedResponse:
        requested_url = url
        current_url = url
        for redirect_number in range(self._max_redirects + 1):
            current_url = await self._url_validator(current_url)
            host = urlsplit(current_url).hostname or ""
            async with self._host_locks[host]:
                async with self._client.stream(
                    "GET", current_url, headers=headers, timeout=timeout_seconds
                ) as response:
                    if allow_not_modified and response.status_code == 304:
                        return FetchedResponse(
                            requested_url=requested_url,
                            final_url=str(response.url),
                            status_code=304,
                            headers=response.headers,
                            content=b"",
                        )

                    if response.is_redirect:
                        location = response.headers.get("location")
                        if not location:
                            raise httpx.HTTPStatusError(
                                "Redirect did not include a Location header",
                                request=response.request,
                                response=response,
                            )
                        if redirect_number >= self._max_redirects:
                            raise httpx.TooManyRedirects(
                                "Maximum redirect count exceeded", request=response.request
                            )
                        current_url = urljoin(str(response.url), location)
                        continue

                    response.raise_for_status()
                    content_type = response.headers.get("content-type", "").split(";", 1)[0].lower()
                    if content_type and content_type not in accepted_types:
                        raise UnexpectedContentTypeError(
                            f"Unexpected Content-Type {content_type!r} from {response.url}"
                        )

                    content_length = response.headers.get("content-length")
                    if content_length and int(content_length) > max_bytes:
                        raise ResponseTooLargeError(
                            f"Response from {response.url} exceeds {max_bytes} bytes"
                        )

                    body = bytearray()
                    async for chunk in response.aiter_bytes():
                        body.extend(chunk)
                        if len(body) > max_bytes:
                            raise ResponseTooLargeError(
                                f"Response from {response.url} exceeds {max_bytes} bytes"
                            )

                    return FetchedResponse(
                        requested_url=requested_url,
                        final_url=str(response.url),
                        status_code=response.status_code,
                        headers=response.headers,
                        content=bytes(body),
                    )

        raise AssertionError("redirect loop terminated unexpectedly")
