from __future__ import annotations

import argparse
import asyncio

import uvicorn

from news_agent.agent import NewsAgentRunner
from news_agent.backend import BackendClient
from news_agent.config import get_settings
from news_agent.schemas import ChatRequest
from news_agent.service import ChatService
from news_agent.sessions import InMemorySessionStore


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run or query the News Agent service.")
    parser.add_argument("query", nargs="*", help="Optional one-shot news question")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8090)
    return parser.parse_args()


async def run_once(query: str) -> None:
    settings = get_settings()
    backend = BackendClient(settings.backend_url, settings.backend_token, settings.request_timeout_seconds)
    try:
        sessions = InMemorySessionStore(
            settings.session_ttl_seconds,
            settings.max_sessions,
            settings.max_history_messages,
        )
        response = await ChatService(NewsAgentRunner(settings, backend), sessions).chat(
            ChatRequest(message=query)
        )
        print(response.answer)
        for index, citation in enumerate(response.citations, start=1):
            print(f"[{index}] {citation.title} — {citation.publisher}: {citation.url}")
    finally:
        await backend.close()


def main() -> None:
    args = parse_args()
    query = " ".join(args.query).strip()
    if query:
        asyncio.run(run_once(query))
        return
    uvicorn.run("news_agent.api:app", host=args.host, port=args.port)


if __name__ == "__main__":
    main()
