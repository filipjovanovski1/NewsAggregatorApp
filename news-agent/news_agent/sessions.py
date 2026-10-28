from __future__ import annotations

import asyncio
import time
import uuid
from dataclasses import dataclass, field

from .schemas import ChatContext


@dataclass
class GroundedArticle:
    article_id: str
    title: str
    publisher: str
    url: str = ""
    published_time: str | None = None
    description: str | None = None
    image_url: str | None = None


@dataclass
class Turn:
    role: str
    content: str
    grounded_articles: list[GroundedArticle] = field(default_factory=list)


@dataclass
class Conversation:
    conversation_id: str
    turns: list[Turn] = field(default_factory=list)
    context: ChatContext | None = None
    touched_at: float = field(default_factory=time.monotonic)


class InMemorySessionStore:
    def __init__(self, ttl_seconds: int, max_sessions: int, max_history_messages: int):
        self._ttl_seconds = ttl_seconds
        self._max_sessions = max_sessions
        self._max_history_messages = max_history_messages
        self._sessions: dict[str, Conversation] = {}
        self._lock = asyncio.Lock()

    async def get_or_create(
        self,
        conversation_id: str | None,
        context: ChatContext | None,
    ) -> Conversation:
        async with self._lock:
            self._remove_expired()
            key = conversation_id if conversation_id in self._sessions else str(uuid.uuid4())
            conversation = self._sessions.get(key)
            if conversation is None:
                if len(self._sessions) >= self._max_sessions:
                    oldest = min(self._sessions.values(), key=lambda item: item.touched_at)
                    self._sessions.pop(oldest.conversation_id, None)
                conversation = Conversation(conversation_id=key)
                self._sessions[key] = conversation
            if context is not None:
                conversation.context = context
            conversation.touched_at = time.monotonic()
            return conversation

    async def append(
        self,
        conversation_id: str,
        user_message: str,
        assistant_message: str,
        grounded_articles: list[GroundedArticle] | None = None,
    ) -> None:
        async with self._lock:
            conversation = self._sessions.get(conversation_id)
            if conversation is None:
                return
            conversation.turns.extend(
                [
                    Turn(role="user", content=user_message),
                    Turn(
                        role="assistant",
                        content=assistant_message,
                        grounded_articles=list(grounded_articles or []),
                    ),
                ]
            )
            conversation.turns = conversation.turns[-self._max_history_messages :]
            conversation.touched_at = time.monotonic()

    async def reset(self, conversation_id: str) -> bool:
        async with self._lock:
            return self._sessions.pop(conversation_id, None) is not None

    def _remove_expired(self) -> None:
        threshold = time.monotonic() - self._ttl_seconds
        expired = [key for key, value in self._sessions.items() if value.touched_at < threshold]
        for key in expired:
            self._sessions.pop(key, None)
