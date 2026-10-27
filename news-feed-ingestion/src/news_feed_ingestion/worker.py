from __future__ import annotations

import asyncio
import logging

from .backend import BackendClient
from .config import Settings
from .models import ClaimRequest, FeedClaim
from .processor import FeedProcessor

logger = logging.getLogger(__name__)


class IngestionWorker:
    def __init__(
        self,
        *,
        settings: Settings,
        backend: BackendClient,
        processor: FeedProcessor,
    ) -> None:
        self._settings = settings
        self._backend = backend
        self._processor = processor
        self._feed_slots = asyncio.Semaphore(settings.max_concurrent_feeds)

    async def run(self, stop: asyncio.Event) -> None:
        logger.info("ingestion worker started", extra={"worker_id": self._settings.worker_id})
        while not stop.is_set():
            try:
                count = await self.run_once()
            except Exception:
                count = 0
                logger.exception(
                    "claim request failed", extra={"worker_id": self._settings.worker_id}
                )

            delay = (
                0
                if count >= self._settings.claim_batch_size
                else self._settings.poll_interval_seconds
            )
            if delay:
                try:
                    await asyncio.wait_for(stop.wait(), timeout=delay)
                except TimeoutError:
                    pass
        logger.info("ingestion worker stopped", extra={"worker_id": self._settings.worker_id})

    async def run_once(self) -> int:
        result = await self._backend.claim(
            ClaimRequest(
                worker_id=self._settings.worker_id,
                max_items=self._settings.claim_batch_size,
                lease_seconds=self._settings.lease_seconds,
            )
        )
        await asyncio.gather(*(self._guarded_process(claim) for claim in result.claims))
        return len(result.claims)

    async def _guarded_process(self, claim: FeedClaim) -> None:
        async with self._feed_slots:
            await self._processor.process(claim)
