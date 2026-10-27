from __future__ import annotations

import os
from dataclasses import dataclass
from functools import lru_cache


def _int_env(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, str(default)))
    except ValueError:
        return default


@dataclass(frozen=True)
class Settings:
    backend_url: str
    backend_token: str
    ollama_host: str
    model: str
    request_timeout_seconds: int
    max_history_messages: int
    session_ttl_seconds: int
    max_sessions: int
    max_articles: int


@lru_cache(maxsize=1)
def get_settings() -> Settings:
    return Settings(
        backend_url=os.getenv("NEWS_BACKEND_URL", "http://localhost:8082").rstrip("/"),
        backend_token=os.getenv("NEWS_BACKEND_TOKEN", ""),
        ollama_host=os.getenv("OLLAMA_HOST", "http://localhost:11434").rstrip("/"),
        model=os.getenv("NEWS_AGENT_MODEL", "gpt-oss:120b-cloud"),
        request_timeout_seconds=max(5, _int_env("NEWS_AGENT_REQUEST_TIMEOUT_SECONDS", 120)),
        max_history_messages=max(2, _int_env("NEWS_AGENT_MAX_HISTORY_MESSAGES", 12)),
        session_ttl_seconds=max(60, _int_env("NEWS_AGENT_SESSION_TTL_SECONDS", 1800)),
        max_sessions=max(10, _int_env("NEWS_AGENT_MAX_SESSIONS", 500)),
        max_articles=min(20, max(1, _int_env("NEWS_AGENT_MAX_ARTICLES", 12))),
    )
