from __future__ import annotations

from typing import Any

from langchain_core.tools import StructuredTool

from .backend import BackendClient


def build_tools(client: BackendClient, max_articles: int) -> list[StructuredTool]:
    async def resolve_scope(q: str) -> dict[str, Any]:
        """Resolve a location phrase into a backend scopeKey. Use when UI context has no scope."""
        return await client.request("POST", "/scope/resolve", json={"q": q})

    async def search_preview(q: str) -> dict[str, Any]:
        """Preview location candidates when a place name is ambiguous."""
        return await client.request("GET", "/search/preview", params={"q": q})

    async def search_articles(
        scope_key: str,
        query: str = "",
        limit: int = 12,
    ) -> dict[str, Any]:
        """Search RSS articles in a resolved geographic scope. Use concise topic keywords."""
        return await client.request(
            "POST",
            "/api/internal/agent/articles/search",
            json={
                "scopeKey": scope_key,
                "query": query,
                "limit": min(max_articles, max(1, limit)),
            },
        )

    async def get_article(article_id: str) -> dict[str, Any]:
        """Load one RSS article by an ID previously returned by search_articles."""
        return await client.request("GET", f"/api/internal/agent/articles/{article_id}")

    return [
        StructuredTool.from_function(coroutine=resolve_scope, name="resolve_scope"),
        StructuredTool.from_function(coroutine=search_preview, name="search_preview"),
        StructuredTool.from_function(coroutine=search_articles, name="search_articles"),
        StructuredTool.from_function(coroutine=get_article, name="get_article"),
    ]
