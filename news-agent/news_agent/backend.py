from __future__ import annotations

from typing import Any

import httpx


class BackendClient:
    def __init__(self, base_url: str, token: str, timeout_seconds: int):
        headers = {"X-Agent-Token": token} if token else {}
        self._client = httpx.AsyncClient(
            base_url=base_url,
            headers=headers,
            timeout=httpx.Timeout(timeout_seconds, connect=5.0),
        )

    async def close(self) -> None:
        await self._client.aclose()

    async def request(
        self,
        method: str,
        path: str,
        *,
        params: dict[str, Any] | None = None,
        json: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        response = await self._client.request(method, path, params=params, json=json)
        response.raise_for_status()
        data = response.json()
        if not isinstance(data, dict):
            raise ValueError(f"Backend returned a non-object response for {path}")
        return data

    async def health(self) -> bool:
        try:
            response = await self._client.get("/_routes", timeout=5.0)
            return response.is_success
        except (httpx.HTTPError, ValueError):
            return False
