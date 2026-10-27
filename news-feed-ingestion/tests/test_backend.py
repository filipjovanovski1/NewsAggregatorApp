from datetime import UTC, datetime
from uuid import uuid4

import httpx
import pytest

from news_feed_ingestion.backend import BackendClient
from news_feed_ingestion.models import ClaimRequest


@pytest.mark.asyncio
async def test_claim_uses_bearer_token_and_camel_case_contract() -> None:
    lease_id = uuid4()
    feed_id = uuid4()
    source_id = uuid4()

    async def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["authorization"] == "Bearer secret"
        assert request.url.path == "/api/internal/feed-ingestion/claims"
        assert request.content == b'{"workerId":"worker-1","maxItems":2,"leaseSeconds":300}'
        return httpx.Response(
            200,
            json={
                "claims": [
                    {
                        "leaseId": str(lease_id),
                        "leaseVersion": 4,
                        "leaseExpiresAt": datetime(2026, 10, 23, tzinfo=UTC).isoformat(),
                        "feedId": str(feed_id),
                        "feedUrl": "https://example.com/feed.xml",
                        "sourceId": str(source_id),
                        "sourceDomain": "example.com",
                    }
                ]
            },
        )

    http = httpx.AsyncClient(
        base_url="https://backend.example/", transport=httpx.MockTransport(handler)
    )
    backend = BackendClient(base_url="https://backend.example", token="secret", client=http)
    result = await backend.claim(ClaimRequest(worker_id="worker-1", max_items=2, lease_seconds=300))

    assert result.claims[0].lease_id == lease_id
    await http.aclose()
