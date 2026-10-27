from __future__ import annotations

import json
import re
from typing import Any
from urllib.parse import unquote

from langchain_core.messages import AIMessage, HumanMessage, ToolMessage

from .agent import NewsAgentRunner
from .backend import BackendClient
from .schemas import ChatContext, ChatRequest, ChatResponse, Citation, ResolvedScope
from .sessions import InMemorySessionStore


ARTICLE_MARKER = re.compile(r"\[article:([0-9a-fA-F-]{36})\]")


def _text_content(value: Any) -> str:
    if isinstance(value, str):
        return value
    if isinstance(value, list):
        parts = [item.get("text", "") for item in value if isinstance(item, dict)]
        return "".join(parts)
    return str(value)


def _tool_payload(message: ToolMessage) -> dict[str, Any] | None:
    if isinstance(message.content, dict):
        return message.content
    if not isinstance(message.content, str):
        return None
    try:
        value = json.loads(message.content)
    except json.JSONDecodeError:
        return None
    return value if isinstance(value, dict) else None


class ChatService:
    def __init__(
        self,
        runner: NewsAgentRunner,
        sessions: InMemorySessionStore,
        backend: BackendClient | None = None,
    ):
        self._runner = runner
        self._sessions = sessions
        self._backend = backend

    async def _effective_context(self, request: ChatRequest) -> ChatContext | None:
        """Let an explicitly named city override the UI's default scope.

        Country-only auto-detection is intentionally not used here because short
        English words such as "is" can also be ISO country codes. The agent can
        still resolve explicit countries through its normal tool workflow.
        """
        if self._backend is None:
            return request.context

        resolved = await self._backend.request("POST", "/scope/resolve", json={"q": request.message})
        if str(resolved.get("kind", "")).lower() != "city" or not resolved.get("scopeKey"):
            return request.context

        return ChatContext(
            scopeKey=str(resolved["scopeKey"]),
            scopeLabel=str(resolved.get("label") or "") or None,
            visibleArticleIds=(request.context.visible_article_ids if request.context else []),
        )

    async def chat(self, request: ChatRequest) -> ChatResponse:
        message = request.message.strip()
        context = await self._effective_context(request)
        conversation = await self._sessions.get_or_create(request.conversation_id, context)

        history = []
        for turn in conversation.turns:
            history.append(HumanMessage(content=turn.content) if turn.role == "user" else AIMessage(content=turn.content))
        history.append(HumanMessage(content=message))

        try:
            result_messages = await self._runner.run(history, conversation.context)
        except RuntimeError:
            # Some local/cloud Ollama models occasionally emit malformed tool
            # arguments. One fresh attempt is safe because all tools are read-only.
            result_messages = await self._runner.run(history, conversation.context)
        final_message = next(
            (item for item in reversed(result_messages) if isinstance(item, AIMessage) and not item.tool_calls),
            None,
        )
        if final_message is None:
            raise RuntimeError("Agent completed without a final answer")

        raw_answer = _text_content(final_message.content).strip()
        articles: dict[str, dict[str, Any]] = {}
        scope: ResolvedScope | None = None

        for item in result_messages:
            if not isinstance(item, ToolMessage):
                continue
            payload = _tool_payload(item)
            if payload is None:
                continue
            if item.name == "resolve_scope" and payload.get("scopeKey"):
                scope = ResolvedScope(scopeKey=str(payload["scopeKey"]), label=payload.get("label"))
            if item.name == "search_articles":
                if payload.get("scopeKey") and scope is None:
                    scope = ResolvedScope(scopeKey=str(payload["scopeKey"]), label=payload.get("scopeLabel"))
                for article in payload.get("items", []):
                    if isinstance(article, dict) and article.get("articleId"):
                        articles[str(article["articleId"]).lower()] = article
            if item.name == "get_article" and payload.get("articleId"):
                articles[str(payload["articleId"]).lower()] = payload

        cited_ids: list[str] = []
        for match in ARTICLE_MARKER.finditer(raw_answer):
            article_id = match.group(1).lower()
            if article_id in articles and article_id not in cited_ids:
                cited_ids.append(article_id)

        citations: list[Citation] = []
        index_by_id: dict[str, int] = {}
        for article_id in cited_ids:
            article = articles[article_id]
            index_by_id[article_id] = len(citations) + 1
            citations.append(
                Citation(
                    articleId=str(article.get("articleId", article_id)),
                    title=str(article.get("title") or "Untitled article"),
                    publisher=str(article.get("publisher") or "Unknown publisher"),
                    url=str(article.get("url") or ""),
                    publishedTime=article.get("publishedTime"),
                )
            )

        def replace_marker(match: re.Match[str]) -> str:
            number = index_by_id.get(match.group(1).lower())
            return f"[{number}]" if number else ""

        answer = ARTICLE_MARKER.sub(replace_marker, raw_answer).strip()
        if conversation.context and conversation.context.scope_key:
            if scope is None:
                scope = ResolvedScope(
                    scopeKey=conversation.context.scope_key,
                    label=conversation.context.scope_label,
                )
            elif (
                unquote(scope.scope_key) == unquote(conversation.context.scope_key)
                and conversation.context.scope_label
            ):
                scope.scope_key = conversation.context.scope_key
                scope.label = conversation.context.scope_label
        await self._sessions.append(conversation.conversation_id, message, answer)

        return ChatResponse(
            conversationId=conversation.conversation_id,
            answer=answer,
            scope=scope,
            citations=citations,
            suggestedQuestions=(
                ["What are the main developments?", "Which sources agree on this story?"]
                if citations
                else []
            ),
        )

    async def reset(self, conversation_id: str) -> bool:
        return await self._sessions.reset(conversation_id)
