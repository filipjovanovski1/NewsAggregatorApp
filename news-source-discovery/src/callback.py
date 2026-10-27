"""Delivers the result payload back to .NET (integration-plan §4.2).

Separate from `main.py` because delivery has its own failure model. The
pipeline's job is done by the time this runs — the payload in hand cost several
minutes of crawling and LLM calls, and losing it because .NET happened to be
restarting is a bad trade. So this retries hard, and when it finally gives up it
writes the payload to disk rather than dropping it.

Retrying is safe because the callback is idempotent on the .NET side: it keys on
`job_id` and returns 200 for a job it has already completed (§4.2).
"""

from __future__ import annotations

import asyncio
import json
import os

import httpx

from src.config import (
    CALLBACK_BACKOFF_BASE,
    CALLBACK_MAX_ATTEMPTS,
    CALLBACK_TIMEOUT,
    CALLBACK_TOKEN_ENV,
)


def _auth_headers() -> dict[str, str]:
    """Bearer token from the environment, if one is configured.

    Absent in local development, where the callback target is a scratch server.
    In deployment its absence is a misconfiguration .NET will reject with 401 —
    which is the correct place for that to surface, rather than here refusing to
    send a payload that may still be wanted.
    """
    token = os.environ.get(CALLBACK_TOKEN_ENV, "").strip()
    return {"Authorization": f"Bearer {token}"} if token else {}


def _should_retry(status_code: int) -> bool:
    """Retry transient failures only.

    5xx and 429 are "try again"; 4xx means .NET rejected the payload itself —
    bad auth, a schema it cannot parse — and resending an identical body five
    more times cannot fix that, it just delays the disk fallback.
    """
    return status_code >= 500 or status_code == 429


async def deliver_result(
    callback_url: str,
    payload: dict,
    fallback_dir: str | None = None,
    max_attempts: int = CALLBACK_MAX_ATTEMPTS,
) -> bool:
    """POST the result to .NET, retrying with exponential backoff.

    Returns True if .NET accepted it. On final failure the payload is written to
    `<fallback_dir>/result.json` (§4.5) so a human can recover it, and False is
    returned — the caller cannot do anything more about it, but the run's own
    output on disk should record that delivery failed.

    `max_attempts` is lowered to 1 by the shutdown drain, where the full backoff
    ladder would not fit inside the termination grace period and being SIGKILLed
    mid-retry sends nothing at all. Everywhere else the default applies: a
    payload that cost minutes of crawling is worth the ladder.
    """
    job_id = payload.get("job_id")
    last: str = "not attempted"
    attempt = 0

    async with httpx.AsyncClient(timeout=CALLBACK_TIMEOUT) as client:
        for attempt in range(1, max_attempts + 1):
            try:
                resp = await client.post(
                    callback_url, json=payload, headers=_auth_headers()
                )
            except Exception as e:
                last = f"{type(e).__name__}: {e}"
            else:
                if resp.status_code < 300:
                    print(f"  Callback delivered ({resp.status_code}) job={job_id}")
                    return True
                last = f"HTTP {resp.status_code}"
                if not _should_retry(resp.status_code):
                    print(f"  [callback] {last} — not retryable, giving up")
                    break

            if attempt < max_attempts:
                delay = CALLBACK_BACKOFF_BASE ** (attempt - 1)
                print(f"  [callback] attempt {attempt} failed ({last}), retry in {delay:.0f}s")
                await asyncio.sleep(delay)

    # `attempt`, not the max: a non-retryable status breaks out on the first
    # try, and logging "after 5 attempts" there sends whoever is reading this
    # looking for four failures that never happened.
    plural = "" if attempt == 1 else "s"
    print(f"  [callback] FAILED after {attempt} attempt{plural}: {last}")
    _write_fallback(fallback_dir, payload)
    return False


def _write_fallback(fallback_dir: str | None, payload: dict) -> None:
    """Last resort: keep the payload on disk (§4.5).

    Best-effort by design — this is already the failure path, and a disk error
    here must not mask the delivery failure that got us here.
    """
    if not fallback_dir:
        return
    try:
        os.makedirs(fallback_dir, exist_ok=True)
        path = os.path.join(fallback_dir, "result.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2, ensure_ascii=False, default=str)
        print(f"  [callback] payload saved to {path}")
    except Exception as e:
        print(f"  [callback] could not save fallback: {type(e).__name__}: {e}")
