from __future__ import annotations

import asyncio
import signal

from .backend import BackendClient
from .config import Settings
from .http import SafeHttpFetcher
from .logging import configure_logging
from .processor import FeedProcessor
from .worker import IngestionWorker


def main() -> None:
    settings = Settings()  # type: ignore[call-arg]
    configure_logging(settings.log_level)
    asyncio.run(_run(settings))


async def _run(settings: Settings) -> None:
    stop = asyncio.Event()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, stop.set)

    backend = BackendClient(
        base_url=str(settings.backend_base_url),
        token=settings.backend_ingestion_token.get_secret_value(),
    )
    fetcher = SafeHttpFetcher(
        user_agent=settings.user_agent,
        max_redirects=settings.max_redirects,
        max_requests_per_host=settings.max_requests_per_host,
    )
    processor = FeedProcessor(settings=settings, backend=backend, fetcher=fetcher)
    worker = IngestionWorker(settings=settings, backend=backend, processor=processor)

    try:
        await worker.run(stop)
    finally:
        await fetcher.aclose()
        await backend.aclose()


if __name__ == "__main__":
    main()
