import asyncio
import json
import os
import time
import uuid
from contextlib import asynccontextmanager
from dataclasses import asdict
from datetime import datetime, timedelta, timezone

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from src.callback import deliver_result
from src.config import (
    CLASSIFY_ERROR_FAIL_RATIO,
    DISCOVER_CACHE_TTL_HOURS,
    EMPTY_QUERY_FAIL_RATIO,
    JOB_QUEUE_MAX,
    JOB_REGISTRY_MAX,
    OUTPUT_DIR,
    PIPELINE_WORKERS,
    RETRY_AFTER_SECONDS,
    REVALIDATE_MAX_BATCH,
    RUN_SCHEMA_VERSION,
    SHUTDOWN_GRACE_SECONDS,
)
from src.models import Candidate, TargetLocation
from src.output import build_result_payload, build_revalidation_payload
from src.pipeline.classifier import classify_candidates
from src.pipeline.crawler import crawl_and_extract
from src.pipeline.discover import discover
from src.pipeline.feeds import revalidate_feeds
from src.pipeline.scorer import score_candidates

@asynccontextmanager
async def _lifespan(app: FastAPI):
    """Own the worker pool for exactly as long as the process serves requests.

    Startup builds the queue here rather than at import so each app instance
    gets a clean one — otherwise a test that leaves an item behind poisons the
    next one, and in production a reload would inherit a queue whose workers are
    gone.
    """
    _start_workers()
    try:
        yield
    finally:
        await _shutdown()


app = FastAPI(lifespan=_lifespan)

STAGES = ("discover", "crawl", "classify", "score")


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


class StageFailure(Exception):
    """A stage completed but produced too little to trust.

    Distinct from a crash: nothing raised, the stage simply degraded past the
    point where its output means anything. Reported as a failure so the caller
    does not mistake it for a location that genuinely yielded nothing.
    """

    def __init__(self, stage: str, message: str):
        super().__init__(message)
        self.stage = stage
        self.message = message


class Run:
    """One pipeline execution and its on-disk trace.

    Scoped by `job_id`, not by location: a 90-day re-run and a retry four
    minutes later must not overwrite each other's files, which is exactly what
    the previous flat `output/<slug>/1_discovery.json` layout did.
    """

    def __init__(self, loc: TargetLocation, job_id: str | None = None):
        self.loc = loc
        self.job_id = job_id or str(uuid.uuid4())
        self.dir = os.path.join(OUTPUT_DIR, loc.slug, self.job_id)
        self.started_at = _utc_now()
        self.finished_at: str | None = None
        self.status = "running"
        self.stage: str | None = None
        self.stages_completed: list[str] = []
        self.warnings: list[str] = []
        self.error: dict | None = None
        self.stats: dict = {}
        os.makedirs(self.dir, exist_ok=True)

    def warn(self, message: str) -> None:
        print(f"  [warn] {message}")
        self.warnings.append(message)

    def manifest(self) -> dict:
        return {
            "schema_version": RUN_SCHEMA_VERSION,
            "job_id": self.job_id,
            "location": asdict(self.loc),
            "status": self.status,
            "stage": self.stage,
            "stages_completed": list(self.stages_completed),
            "started_at": self.started_at,
            "updated_at": _utc_now(),
            "finished_at": self.finished_at,
            "stats": self.stats,
            "warnings": list(self.warnings),
            "error": self.error,
        }

    def _write_manifest(self) -> None:
        path = os.path.join(self.dir, "run.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(self.manifest(), f, indent=2, ensure_ascii=False, default=str)

    def begin(self, stage: str) -> None:
        self.stage = stage
        self._write_manifest()

    def finish(self, stage: str, filename: str, candidates: list[Candidate]) -> None:
        """Record a completed stage and save its candidates."""
        path = os.path.join(self.dir, filename)
        data = {
            "location": asdict(self.loc),
            "job_id": self.job_id,
            "candidates": [asdict(c) for c in candidates],
        }
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False, default=str)
        print(f"  Saved {path} ({len(candidates)} candidates)")

        self.stages_completed.append(stage)
        self._write_manifest()

    def completed(self) -> None:
        self.status = "completed"
        self.stage = None
        self.finished_at = _utc_now()
        self._write_manifest()

    def failed(self, stage: str, exc: BaseException) -> None:
        self.status = "failed"
        self.stage = stage
        self.finished_at = _utc_now()
        self.error = {
            "stage": stage,
            "type": type(exc).__name__,
            "message": str(exc),
        }
        self._write_manifest()
        print(f"\n  [FAILED] {stage}: {type(exc).__name__}: {exc}")


def _discover_cache_path(loc: TargetLocation) -> str:
    return os.path.join(OUTPUT_DIR, loc.slug, "discover_cache.json")


def _load_discover_cache(loc: TargetLocation) -> list[Candidate] | None:
    path = _discover_cache_path(loc)
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError):
        return None

    written = data.get("written_at")
    if not written:
        return None
    try:
        age = datetime.now(timezone.utc) - datetime.fromisoformat(written.replace("Z", "+00:00"))
    except ValueError:
        return None
    if age > timedelta(hours=DISCOVER_CACHE_TTL_HOURS):
        return None

    # Only the flat fields discovery itself sets are rehydrated. The nested
    # signals/classification/scoring are always absent at this point, and
    # reconstructing them would make this a general deserializer, which it is
    # explicitly not.
    flat = ("domain", "url", "homepage_url", "title", "description",
            "queries", "search_occurrences")
    candidates = [
        Candidate(**{k: v for k, v in c.items() if k in flat})
        for c in data.get("candidates", [])
    ]
    return candidates or None


def _save_discover_cache(loc: TargetLocation, candidates: list[Candidate]) -> None:
    path = _discover_cache_path(loc)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    data = {
        "written_at": _utc_now(),
        "location": asdict(loc),
        "candidates": [asdict(c) for c in candidates],
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2, ensure_ascii=False, default=str)


async def _run_discover(run: Run, use_cache: bool) -> list[Candidate]:
    if use_cache:
        cached = _load_discover_cache(run.loc)
        if cached:
            print(f"\n  Reusing cached discovery for {run.loc.slug} ({len(cached)} candidates)")
            run.warnings.append(f"discover: reused cache ({len(cached)} candidates)")
            run.stats["discovered"] = len(cached)
            return cached

    stats: dict = {}
    results = await asyncio.to_thread(discover, run.loc, stats)

    run.stats["discovered"] = len(results)
    run.stats["queries_run"] = stats.get("queries_run", 0)
    run.stats["queries_empty"] = stats.get("queries_empty", 0)

    ran, empty = stats.get("queries_run", 0), stats.get("queries_empty", 0)
    if ran and empty / ran >= EMPTY_QUERY_FAIL_RATIO:
        raise StageFailure(
            "discover",
            f"{empty}/{ran} searches returned nothing — treating as rate-limited "
            f"rather than as a location with no news sources",
        )
    if empty:
        run.warn(f"discover: {empty}/{ran} queries returned no results")

    if not results:
        run.warn("discover: no candidates survived the hard filters")

    _save_discover_cache(run.loc, results)
    return results


async def _run_classify(run: Run, candidates: list[Candidate]) -> list[Candidate]:
    classified = await asyncio.to_thread(classify_candidates, candidates)

    errored = sum(1 for c in classified if c.classification and c.classification.error)
    run.stats["classify_errors"] = errored
    run.stats["classified"] = {
        label: sum(
            1 for c in classified
            if c.classification and c.classification.classification == label
        )
        for label in ("NEWS_SOURCE", "DISCOVERY_SOURCE", "REJECT")
    }

    if classified and errored / len(classified) >= CLASSIFY_ERROR_FAIL_RATIO:
        raise StageFailure(
            "classify",
            f"{errored}/{len(classified)} classifications failed — the model is "
            f"unreachable or erroring, so these are defaults rather than verdicts",
        )
    if errored:
        run.warn(f"classify: {errored}/{len(classified)} classifications failed")

    return classified



async def run_pipeline(
    loc: TargetLocation,
    job_id: str | None = None,
    use_discover_cache: bool = True,
) -> tuple[dict, list[Candidate]]:
    """Run all four stages, returning `(manifest, candidates)`.

    Never raises for a pipeline failure: the manifest carries `status` and
    `error` instead, because a job that dies silently leaves the .NET row in
    `Running` forever, indistinguishable from one still crawling. Only a
    failure to construct the `Run` itself escapes.
    """
    run = Run(loc, job_id)
    print(f"\nJob {run.job_id} → {run.dir}")

    candidates: list[Candidate] = []
    stage = "discover"
    try:
        run.begin("discover")
        candidates = await _run_discover(run, use_discover_cache)
        run.finish("discover", "1_discovery.json", candidates)

        stage = "crawl"
        run.begin(stage)
        candidates = await crawl_and_extract(candidates)
        run.stats["crawl_failures"] = sum(
            1 for c in candidates if not (c.signals and c.signals.crawl_success)
        )
        run.finish(stage, "2_crawl.json", candidates)

        stage = "classify"
        run.begin(stage)
        candidates = await _run_classify(run, candidates)
        run.finish(stage, "3_classification.json", candidates)

        stage = "score"
        run.begin(stage)
        candidates = await asyncio.to_thread(score_candidates, candidates, loc)
        run.finish(stage, "4_scoring.json", candidates)

        run.completed()
    except StageFailure as e:
        run.failed(e.stage, e)
    except Exception as e:
        run.failed(stage, e)

    return run.manifest(), candidates


@app.post("/get_sources/")
async def get_sources(location: TargetLocation):
    """Synchronous run, kept for manual testing. .NET uses POST /jobs."""
    manifest, candidates = await run_pipeline(location)
    return {**manifest, "candidates": [asdict(c) for c in candidates]}


# --- Job queue and registry (integration-plan §4.1) -------------------------
#
# In-process and deliberately not durable: the .NET `DiscoveryTarget` row is the
# record of what still needs running (§6.7), and this is a *buffer* in front of
# it plus dedup for the current process. That distinction is the whole design —
# a queue deep enough to hold the §8 bootstrap would be a second, non-durable
# answer to "what is pending", and after a restart nobody can reconcile the two.
#
# So the queue is short, .NET keeps ownership, and every accepted job is
# guaranteed a callback — including on shutdown, which is what lets §4.3's sweep
# drop from a 30-minute deadline to a long-horizon safety net.

_JOBS: dict[str, dict] = {}
_QUEUE: asyncio.Queue = asyncio.Queue(maxsize=JOB_QUEUE_MAX)
_WORKERS: list[asyncio.Task] = []
# Set False by shutdown so admission stops before the drain runs. A job accepted
# after the drain has taken the queue would never run and never call back.
_ACCEPTING = True


def _active_jobs() -> int:
    """Jobs a worker is executing — what `PIPELINE_WORKERS` caps."""
    return sum(1 for j in _JOBS.values() if j["status"] == "running")


def _queued_jobs() -> int:
    return _QUEUE.qsize()


def _unfinished_jobs() -> int:
    """Jobs that have started and not yet called back.

    Deliberately wider than `_active_jobs()`: it counts `delivering` too. The
    two answer different questions — admission asks "is a worker free", which is
    true the moment the crawl ends, while shutdown asks "has .NET heard about
    this", which is not true until the callback lands. Using the admission
    predicate here would cancel a worker mid-callback and discard a finished
    payload that cost minutes of crawling.
    """
    return sum(1 for j in _JOBS.values() if j["status"] in ("running", "delivering"))


def _prune_registry() -> None:
    """Bound the registry so a long-lived process does not leak it.

    Finished entries are kept only to answer "I have seen this job_id" with a
    409; past the cap the oldest lose that protection, which is harmless — .NET
    retries carry a fresh job_id per attempt (§4.5).
    """
    if len(_JOBS) <= JOB_REGISTRY_MAX:
        return
    finished = [k for k, v in _JOBS.items() if v["status"] == "done"]
    for job_id in finished[: len(_JOBS) - JOB_REGISTRY_MAX]:
        del _JOBS[job_id]


class JobRequest(BaseModel):
    """The §4.1 request: a `TargetLocation` plus the job's own metadata."""

    job_id: str
    callback_url: str
    iso2: str
    country_name: str = ""
    iso3: str | None = None
    city: str | None = None
    city_local_name: str | None = None
    city_id: str | None = None
    known_domains: list[str] = []

    def location(self) -> TargetLocation:
        return TargetLocation(
            iso2=self.iso2.upper(),
            country_name=self.country_name,
            iso3=self.iso3,
            city=self.city,
            city_local_name=self.city_local_name,
            city_id=self.city_id,
        )


def _failure_payload(
    job_id: str, loc: TargetLocation, stage: str, error_type: str, message: str
) -> dict:
    """A §2.2 failure payload for a job that produced no manifest at all.

    Built through `build_result_payload` rather than by hand so these paths and
    the ordinary path emit the same envelope — .NET should not need a second
    deserializer for the cases where things went worst.
    """
    now = _utc_now()
    return build_result_payload(
        {
            "job_id": job_id,
            "status": "failed",
            "location": asdict(loc),
            "started_at": now,
            "finished_at": now,
            "stats": {},
            "warnings": [],
            "error": {"stage": stage, "type": error_type, "message": message},
        },
        [],
    )


def _crash_payload(job_id: str, loc: TargetLocation, exc: BaseException) -> dict:
    """For a crash that escaped `run_pipeline()`."""
    return _failure_payload(job_id, loc, "runner", type(exc).__name__, str(exc))


async def _run_job(job_id: str, req: JobRequest) -> None:
    """Run the pipeline, then deliver the result. Always does both."""
    loc = req.location()
    run_dir = os.path.join(OUTPUT_DIR, loc.slug, job_id)
    known = {d.lower().strip() for d in req.known_domains if d.strip()}
    _JOBS[job_id]["status"] = "running"
    _JOBS[job_id]["started_at"] = _utc_now()

    try:
        try:
            manifest, candidates = await run_pipeline(loc, job_id=job_id)
            payload = build_result_payload(manifest, candidates, known)
        except asyncio.CancelledError:
            raise
        except Exception as e:
            # run_pipeline() catches its own stage failures, so reaching here
            # means the runner itself broke. .NET still gets a verdict: without
            # one its row sits `Running` until the §4.3 sweep, indistinguishable
            # from a job still crawling.
            print(f"  [runner] {type(e).__name__}: {e}")
            payload = _crash_payload(job_id, loc, e)
        finally:
            # Free the worker before delivering. Callback retries can run for
            # minutes against an unreachable .NET, and holding a worker through
            # that would stall the queue for no reason — delivery is httpx only,
            # no browser.
            _JOBS[job_id]["status"] = "delivering"

        await deliver_result(req.callback_url, payload, run_dir)
    except asyncio.CancelledError:
        # Shutdown cancelled us mid-flight. Deliberately *not* marked done:
        # `_shutdown` hands back every job that is not, and one that marked
        # itself done on the way out would disappear with no callback at all —
        # the exact silent loss the drain exists to prevent.
        _JOBS[job_id]["status"] = "cancelled"
        raise
    else:
        _JOBS[job_id]["status"] = "done"
        # The request is only kept so shutdown can call back on a job that never
        # ran. This one has called back already, and the registry outlives it by
        # up to JOB_REGISTRY_MAX entries.
        _JOBS[job_id].pop("request", None)
    finally:
        _prune_registry()


async def _worker(n: int) -> None:
    """Pull jobs off the queue forever, one at a time.

    The blanket `except` is not defensive clutter: `_run_job` has its own
    try/finally and should never raise, but if it ever does, an unguarded worker
    dies silently and the pool shrinks. At `PIPELINE_WORKERS = 1` that means
    every subsequent job is accepted with a 202 and then never runs — the queue
    fills, admission starts returning 429 forever, and `/health` reports zero
    active jobs throughout. A worker that logs and keeps draining is the only
    version of this that fails visibly.
    """
    while True:
        job_id, req = await _QUEUE.get()
        try:
            await _run_job(job_id, req)
        except asyncio.CancelledError:
            raise
        except Exception as e:
            # Not cancellation — the run is genuinely over and nothing more can
            # be delivered for it, so it is `done` rather than handed back.
            print(f"  [worker {n}] {type(e).__name__}: {e} (job={job_id})")
            entry = _JOBS.get(job_id)
            if entry is not None:
                entry["status"] = "done"
                entry.pop("request", None)
        finally:
            _QUEUE.task_done()


def _start_workers() -> None:
    """Fresh queue and pool per app start (see `_lifespan`)."""
    global _QUEUE, _ACCEPTING
    _QUEUE = asyncio.Queue(maxsize=JOB_QUEUE_MAX)
    _ACCEPTING = True
    _WORKERS.clear()
    # asyncio only holds a weak reference to a bare task, so a worker can be
    # garbage-collected mid-crawl. Holding these references is not optional.
    _WORKERS.extend(
        asyncio.create_task(_worker(n), name=f"job-worker-{n}")
        for n in range(PIPELINE_WORKERS)
    )


def _take_queued() -> list[tuple[str, "JobRequest"]]:
    """Empty the queue synchronously, so no worker can race the drain.

    Every `get_nowait` here happens without an intervening `await`, which is
    what guarantees a worker blocked on `_QUEUE.get()` cannot claim an item this
    is about to hand back to .NET.
    """
    taken: list[tuple[str, JobRequest]] = []
    while True:
        try:
            taken.append(_QUEUE.get_nowait())
        except asyncio.QueueEmpty:
            return taken
        _QUEUE.task_done()


async def _shutdown() -> None:
    """Hand every accepted job back to .NET before the process dies.

    The queue is not durable, so shutdown is the moment that promise is kept or
    broken. Jobs that never started are handed back immediately — re-dispatching
    them costs nothing. A job already crawling gets a grace period first, since
    its `discover_cache.json` lives on a disk no volume outlives, so a restart
    makes it pay the rate-limited search stage again.

    Delivery uses a single attempt: the full backoff ladder does not fit inside
    a termination grace period, and being SIGKILLed mid-retry sends nothing.
    """
    global _ACCEPTING
    _ACCEPTING = False

    orphans = _take_queued()
    if orphans:
        print(f"  [shutdown] handing back {len(orphans)} queued job(s)")
        await asyncio.gather(
            *(_hand_back(job_id, req, "queued") for job_id, req in orphans),
            return_exceptions=True,
        )

    deadline = time.monotonic() + SHUTDOWN_GRACE_SECONDS
    while _unfinished_jobs() and time.monotonic() < deadline:
        await asyncio.sleep(0.05)

    for w in _WORKERS:
        w.cancel()
    await asyncio.gather(*_WORKERS, return_exceptions=True)
    _WORKERS.clear()

    # Anything still unfinished was cancelled mid-flight. A job in `delivering`
    # may already have landed, and telling .NET it failed would be wrong except
    # that §4.2 makes the callback idempotent on `job_id`: a job it has already
    # completed returns 200 and changes nothing. Sending is therefore safe, and
    # staying silent is not — silence is the case §4.3 has to time out.
    stranded = [
        (job_id, j)
        for job_id, j in _JOBS.items()
        if j["status"] not in ("done", "queued") and j.get("request")
    ]
    if stranded:
        print(f"  [shutdown] {len(stranded)} job(s) cancelled in flight")
        # return_exceptions: one entry that cannot be delivered must not cost the
        # others their callback. This is the last chance any of them gets.
        await asyncio.gather(
            *(_hand_back(job_id, j["request"], j["status"]) for job_id, j in stranded),
            return_exceptions=True,
        )


async def _hand_back(job_id: str, req: "JobRequest", stage: str) -> None:
    """Tell .NET this job will not finish here, so it can re-dispatch now."""
    loc = req.location()
    payload = _failure_payload(
        job_id,
        loc,
        stage,
        "PipelineShutdown",
        "pipeline shut down before this job completed; safe to re-dispatch",
    )
    _JOBS[job_id]["status"] = "done"
    await deliver_result(
        req.callback_url,
        payload,
        os.path.join(OUTPUT_DIR, loc.slug, job_id),
        max_attempts=1,
    )


@app.post("/jobs", status_code=202)
async def create_job(req: JobRequest):
    """Accept a discovery job (§4.1). 202 means *accepted*, not *succeeded*.

    Accepted jobs are queued, not necessarily started — a worker picks each one
    up when it frees. Everything below happens without an `await`, which is what
    makes the depth test atomic on a single event loop: two requests arriving
    together cannot both pass it.
    """
    iso2 = req.iso2.strip().upper()
    if len(iso2) != 2 or not iso2.isalpha():
        # 400 is severe on the .NET side — it sets `IsEnabled = false`, because
        # bad input will not fix itself. So this checks the *shape* of the code
        # only. A well-formed code missing from COUNTRY_TLDS/COUNTRY_LANGUAGES
        # is not an error: it runs and forfeits 30 scoring points, which the
        # scorer already warns about. Rejecting those would permanently disable
        # the ~209 countries the config dicts do not cover yet.
        raise HTTPException(status_code=400, detail=f"iso2 must be a 2-letter code, got {req.iso2!r}")

    if req.job_id in _JOBS:
        raise HTTPException(status_code=409, detail=f"job {req.job_id} already known")

    target = req.location().slug
    if any(j["target"] == target and j["status"] != "done" for j in _JOBS.values()):
        raise HTTPException(status_code=409, detail=f"a job for {target} is already in flight")

    if not _ACCEPTING:
        # Shutting down. 503 rather than 429: the distinction is "come back to
        # this instance later" versus "this instance is going away", and only
        # the second should survive a `Retry-After` the pod will not outlive.
        raise HTTPException(status_code=503, detail="pipeline is shutting down")

    if _queued_jobs() >= JOB_QUEUE_MAX:
        # Backpressure, not failure: .NET leaves the job Pending and adds
        # Retry-After to NextDueAt. The queue makes this rare — it no longer
        # fires merely because a job is running — but it does not make it
        # impossible, and a bounded queue is the point. Treating it as a failure
        # during the §8 bootstrap would still disable most of the world.
        raise HTTPException(
            status_code=429,
            detail="job queue is full",
            headers={"Retry-After": str(RETRY_AFTER_SECONDS)},
        )

    _JOBS[req.job_id] = {
        "status": "queued",
        "target": target,
        "queued_at": _utc_now(),
        "started_at": None,
        # Kept so shutdown can call back on a job it never ran: the callback_url
        # arrived on the request and exists nowhere else in this process.
        "request": req,
    }
    _QUEUE.put_nowait((req.job_id, req))

    # `queue_position` is the buffer depth including this job, so 1 means it is
    # next. Informational — .NET learns the outcome from the callback, not by
    # watching this number.
    return {"accepted": True, "job_id": req.job_id, "queue_position": _queued_jobs()}


@app.get("/health")
async def health():
    """Lets the dispatcher see depth before it dispatches (§4.1).

    `queued` is the number waiting, `active_jobs` the number a worker holds.
    Advisory only — it is a snapshot, so the dispatcher still has to handle the
    429 it races with.
    """
    return {
        "active_jobs": _active_jobs(),
        "workers": PIPELINE_WORKERS,
        "queued": _queued_jobs(),
        "queue_capacity": JOB_QUEUE_MAX,
        "accepting": _ACCEPTING,
    }


class FeedToValidate(BaseModel):
    """One stored feed .NET wants re-checked.

    `source_feed_id` is opaque here and echoed back untouched, the same contract
    as `city_id`. `domain` is optional: it is the site the feed belongs to, used
    to decide which entries count as external. Send it when known — a feed
    hosted on feedburner or substack measures ~100% external against its own
    host and reads as an aggregator otherwise.
    """

    source_feed_id: object
    url: str
    domain: str | None = None


class ValidateFeedsRequest(BaseModel):
    feeds: list[FeedToValidate]


@app.post("/feeds/validate")
async def validate_feeds_endpoint(request: ValidateFeedsRequest):
    """Re-check stored feeds (integration-plan §4.4).

    Synchronous by design: this is httpx plus feedparser, seconds not minutes,
    so it needs no job row and no callback.

    **It must never take the Phase 2 capacity semaphore.** That gate exists
    because the pipeline runs one Chromium at a time; nothing here launches a
    browser, and gating it would let nightly revalidation block discovery for no
    reason.
    """
    if not request.feeds:
        raise HTTPException(status_code=400, detail="feeds must not be empty")
    if len(request.feeds) > REVALIDATE_MAX_BATCH:
        raise HTTPException(
            status_code=400,
            detail=f"at most {REVALIDATE_MAX_BATCH} feeds per request, got {len(request.feeds)}",
        )

    results = await revalidate_feeds([(f.url, f.domain) for f in request.feeds])
    # revalidate_feeds guarantees one result per input, in order — so zipping on
    # position is safe, and is the only thing that is: a valid feed comes back
    # under its post-redirect URL.
    return build_revalidation_payload(
        list(zip((f.source_feed_id for f in request.feeds), results))
    )


def _print_summary(manifest: dict, candidates: list[Candidate]) -> None:
    if manifest["status"] == "failed":
        err = manifest["error"]
        print(f"\n{'=' * 60}")
        print(f"  RUN FAILED in {err['stage']}: {err['type']}")
        print(f"  {err['message']}")
        print(f"{'=' * 60}")
        return

    for label in ("NEWS_SOURCE", "DISCOVERY_SOURCE", "REJECT"):
        group = [
            c for c in candidates
            if c.classification and c.classification.classification == label
        ]
        if not group:
            continue
        print(f"\n{'=' * 60}")
        print(f"  {label} ({len(group)})")
        print(f"{'=' * 60}")
        for r in group:
            cl = r.classification
            if cl is None:
                continue
            signals = r.signals
            sc = r.scoring
            rss = len(signals.rss_feeds) if signals else 0
            articles = signals.article_like_paths if signals else 0
            lang = (signals.language if signals else "") or "-"
            score_str = f"score={sc.score:5.1f} [{sc.polling_tier}]" if sc else ""
            print(
                f"  {r.domain:<35} conf={cl.confidence:.2f}  "
                f"rss={rss}  articles={articles}  lang={lang}  {score_str}"
            )
            print(f"    {cl.reason}")

    if manifest["warnings"]:
        print(f"\n{'=' * 60}")
        print("  WARNINGS")
        print(f"{'=' * 60}")
        for w in manifest["warnings"]:
            print(f"  - {w}")


def main():
    # Stands in for the request payload the .NET backend will post. `iso2` is
    # the identity; the names are search-query text only.
    location = TargetLocation(
        iso2="MK",
        iso3="MKD",
        country_name="North Macedonia",
        city="Skopje",
        city_local_name="Скопје",
    )

    manifest, candidates = asyncio.run(run_pipeline(location))
    _print_summary(manifest, candidates)
    return 0 if manifest["status"] == "completed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
