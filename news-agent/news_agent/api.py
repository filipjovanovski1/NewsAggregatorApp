from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, HTTPException, Request, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from .agent import NewsAgentRunner
from .backend import BackendClient
from .config import get_settings
from .schemas import ChatRequest, ChatResponse, HealthResponse
from .service import ChatService
from .sessions import InMemorySessionStore


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = get_settings()
    backend = BackendClient(settings.backend_url, settings.backend_token, settings.request_timeout_seconds)
    sessions = InMemorySessionStore(
        settings.session_ttl_seconds,
        settings.max_sessions,
        settings.max_history_messages,
    )
    app.state.backend = backend
    app.state.chat_service = ChatService(NewsAgentRunner(settings, backend), sessions, backend)
    yield
    await backend.close()


app = FastAPI(
    title="News Agent",
    version="1.0.0",
    description="Grounded chatbot over RSS articles stored by NewsAggregatorApp.",
    lifespan=lifespan,
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173", "http://localhost:8080"],
    allow_methods=["GET", "POST", "DELETE"],
    allow_headers=["*"],
)


@app.get("/health")
async def health() -> dict[str, str]:
    """Process liveness check; dependency checks live at /ready."""
    return {"status": "ok"}


async def _ollama_is_ready(base_url: str) -> bool:
    try:
        async with httpx.AsyncClient(timeout=5.0) as client:
            response = await client.get(f"{base_url}/api/tags")
            return response.is_success
    except httpx.HTTPError:
        return False


@app.get("/ready", response_model=HealthResponse)
async def ready(request: Request):
    settings = get_settings()
    backend_ok, ollama_ok = await asyncio.gather(
        request.app.state.backend.health(),
        _ollama_is_ready(settings.ollama_host),
    )
    result = HealthResponse(
        status="ok" if backend_ok and ollama_ok else "degraded",
        backend="ok" if backend_ok else "unavailable",
        ollama="ok" if ollama_ok else "unavailable",
        model=settings.model,
    )
    if result.status != "ok":
        return JSONResponse(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            content=result.model_dump(),
        )
    return result


@app.post("/v1/chat", response_model=ChatResponse, response_model_by_alias=True)
async def chat(payload: ChatRequest, request: Request) -> ChatResponse:
    try:
        return await request.app.state.chat_service.chat(payload)
    except httpx.TimeoutException as exc:
        raise HTTPException(status_code=status.HTTP_504_GATEWAY_TIMEOUT, detail="A dependency timed out") from exc
    except httpx.HTTPStatusError as exc:
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail="Backend request failed") from exc
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail="A dependency is unavailable") from exc
    except RuntimeError as exc:
        raise HTTPException(status_code=status.HTTP_502_BAD_GATEWAY, detail=str(exc)) from exc


@app.delete("/v1/conversations/{conversation_id}", status_code=status.HTTP_204_NO_CONTENT)
async def reset_conversation(conversation_id: str, request: Request) -> None:
    await request.app.state.chat_service.reset(conversation_id)
