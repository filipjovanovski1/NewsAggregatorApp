import pytest

from news_feed_ingestion.urls import (
    UnsafeUrlError,
    normalize_url,
    same_site,
    validate_public_url,
    validate_resolved_addresses,
)


def test_normalize_url_removes_tracking_and_fragment() -> None:
    assert normalize_url(
        "HTTPS://Example.COM:443/story?utm_source=rss&id=42&fbclid=x#comments"
    ) == "https://example.com/story?id=42"


def test_normalize_url_preserves_unknown_query_parameters() -> None:
    assert normalize_url("https://example.com/?article=1&lang=mk") == (
        "https://example.com/?article=1&lang=mk"
    )


def test_private_address_is_rejected() -> None:
    with pytest.raises(UnsafeUrlError):
        validate_resolved_addresses(["169.254.169.254"])


@pytest.mark.asyncio
async def test_public_resolver_is_used() -> None:
    async def resolver(host: str, port: int) -> list[str]:
        assert (host, port) == ("example.com", 443)
        return ["93.184.216.34"]

    assert await validate_public_url("https://example.com", resolver) == "https://example.com/"


def test_same_site_allows_www_variant_only() -> None:
    assert same_site("https://www.example.com/a", "https://example.com/b")
    assert not same_site("https://cdn.example.com/a", "https://example.com/b")
