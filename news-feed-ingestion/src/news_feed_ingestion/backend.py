from __future__ import annotations

import asyncio
from uuid import UUID

import httpx

from .models import ClaimRequest, ClaimResponse, CompleteRequest, FailureRequest


class BackendClient:
    def __init__(
        self,
        *,
        base_url: str,
        token: str,
        client: httpx.AsyncClient | None = None,
        delivery_attempts: int = 4,
    ) -> None:
        self._owns_client = client is None
        self._headers = {"Authorization": f"Bearer {token}"}
        self._client = client or httpx.AsyncClient(
            base_url=base_url.rstrip("/") + "/",
            headers=self._headers,
            timeout=30,
        )
        self._delivery_attempts = delivery_attempts

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def claim(self, request: ClaimRequest) -> ClaimResponse:
        response = await self._request(
            "POST",
            "api/internal/feed-ingestion/claims",
            json=request.model_dump(mode="json", by_alias=True),
        )
        return ClaimResponse.model_validate(response.json())

    async def complete(self, lease_id: UUID, request: CompleteRequest) -> None:
        await self._request(
            "POST",
            f"api/internal/feed-ingestion/claims/{lease_id}/complete",
            headers={"Idempotency-Key": f"{lease_id}:{request.lease_version}"},
            json=request.model_dump(mode="json", by_alias=True, exclude_none=True),
        )

    async def fail(self, lease_id: UUID, request: FailureRequest) -> None:
        await self._request(
            "POST",
            f"api/internal/feed-ingestion/claims/{lease_id}/fail",
            json=request.model_dump(mode="json", by_alias=True, exclude_none=True),
        )

    async def _request(self, method: str, url: str, **kwargs: object) -> httpx.Response:
        request_headers = dict(self._headers)
        request_headers.update(kwargs.pop("headers", {}))  # type: ignore[arg-type]
        kwargs["headers"] = request_headers
        for attempt in range(1, self._delivery_attempts + 1):
            try:
                response = await self._client.request(method, url, **kwargs)
                if response.status_code != 429 and response.status_code < 500:
                    response.raise_for_status()
                    return response
                response.raise_for_status()
            except (httpx.TransportError, httpx.HTTPStatusError):
                if attempt == self._delivery_attempts:
                    raise
                await asyncio.sleep(min(2 ** (attempt - 1), 8))
        raise AssertionError("retry loop terminated unexpectedly")
