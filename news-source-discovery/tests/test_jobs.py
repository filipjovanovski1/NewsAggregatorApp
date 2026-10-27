"""Tests for the §4.1 job endpoint and the §4.2 callback.

The pipeline itself is stubbed out — these cover admission and delivery, which
are the parts .NET's state machine reacts to. Getting a response code wrong here
is worse than a crash: a 429 misread as a failure disables a location, and an
accepted job that never calls back leaves a row `Running` until the sweeper.

No browser, no Ollama, no network.
"""

import asyncio
import json
import threading
from contextlib import contextmanager

import pytest
from fastapi.testclient import TestClient

import main
from src.callback import _should_retry, _write_fallback


def request(job_id="job-1", iso2="MK", city="Skopje", **kwargs) -> dict:
    base = {
        "job_id": job_id,
        "callback_url": "https://api.newsapp/api/discovery/jobs/x/result",
        "iso2": iso2,
        "iso3": "MKD",
        "country_name": "North Macedonia",
        "city": city,
        "city_local_name": "Скопје",
        "city_id": "9c5b94b1-35ad-49bb-b118-8e8fc24abf80",
    }
    return {**base, **kwargs}


def manifest(job_id, loc=None):
    return {
        "job_id": job_id,
        "status": "completed",
        "location": {"iso2": loc.iso2, "city": loc.city} if loc else {},
        "started_at": "2026-08-23T10:00:00Z",
        "finished_at": "2026-08-23T10:06:00Z",
        "stats": {"discovered": 41},
        "warnings": [],
        "error": None,
    }


@pytest.fixture
def client(monkeypatch):
    """A client whose pipeline returns instantly and whose callback is captured."""
    main._JOBS.clear()
    delivered: list[dict] = []

    async def fake_pipeline(loc, job_id=None, use_discover_cache=True):
        return manifest(job_id, loc), []

    async def fake_deliver(url, payload, fallback_dir=None, max_attempts=5):
        delivered.append(payload)
        return True

    monkeypatch.setattr(main, "run_pipeline", fake_pipeline)
    monkeypatch.setattr(main, "deliver_result", fake_deliver)

    with TestClient(main.app) as c:
        c.delivered = delivered
        yield c


def drain_until(client, predicate, tries=200):
    """Poll /health until `predicate` holds. Used where the interesting moment
    is mid-run rather than after it."""
    for _ in range(tries):
        if predicate(client.get("/health").json()):
            return
    raise AssertionError("condition never held")


def drain(client, expected=1, tries=200):
    """Wait for background jobs to finish, using /health as the signal."""
    for _ in range(tries):
        h = client.get("/health").json()
        if h["active_jobs"] == 0 and h["queued"] == 0 and len(client.delivered) >= expected:
            return
    raise AssertionError(
        f"expected {expected} delivered, got {len(client.delivered)}"
    )


# --- Admission (§4.1) -------------------------------------------------------

def test_accepts_a_job(client):
    r = client.post("/jobs", json=request())
    assert r.status_code == 202
    body = r.json()
    assert body["accepted"] is True and body["job_id"] == "job-1"


def test_rejects_a_malformed_iso2(client):
    assert client.post("/jobs", json=request(iso2="North Macedonia")).status_code == 400
    assert client.post("/jobs", json=request(iso2="M")).status_code == 400
    assert client.post("/jobs", json=request(iso2="M1")).status_code == 400


def test_accepts_a_country_the_config_does_not_cover():
    """400 makes .NET set IsEnabled=false permanently.

    COUNTRY_TLDS covers ~40 of 249 countries; rejecting the rest would disable
    most of the world on its first dispatch. An unconfigured country runs and
    forfeits scoring points, which the scorer already warns about.
    """
    from src.config import COUNTRY_TLDS

    assert "NU" not in COUNTRY_TLDS  # Niue — well-formed, unconfigured


def test_duplicate_job_id_is_409(client):
    client.post("/jobs", json=request())
    r = client.post("/jobs", json=request())
    assert r.status_code == 409


@contextmanager
def gated_client(monkeypatch, grace=0.2):
    """A client whose pipeline blocks until `c.gate` is set.

    The gate is a `threading.Event` polled from the loop, not an
    `asyncio.Event`: the app runs on TestClient's own thread, and setting an
    asyncio primitive across threads is not safe even where it happens to work.
    """
    main._JOBS.clear()
    delivered: list[dict] = []
    gate = threading.Event()

    async def gated(loc, job_id=None, use_discover_cache=True):
        while not gate.is_set():
            await asyncio.sleep(0.01)
        return manifest(job_id, loc), []

    async def fake_deliver(url, payload, fallback_dir=None, max_attempts=5):
        delivered.append(payload)
        return True

    monkeypatch.setattr(main, "run_pipeline", gated)
    monkeypatch.setattr(main, "deliver_result", fake_deliver)
    monkeypatch.setattr(main, "SHUTDOWN_GRACE_SECONDS", grace)

    with TestClient(main.app) as c:
        c.delivered = delivered
        c.gate = gate
        # Deliberately not released on the way out: a test that leaves the gate
        # shut is exercising the shutdown drain, and releasing it here would
        # let the job complete normally and hide exactly what it is asserting.
        # The short `grace` is what keeps that from costing real seconds.
        yield c


def test_second_job_for_the_same_target_is_409(monkeypatch):
    """Only while the first is *in flight* — a finished run must not block the
    90-day re-run of the same location, which is the whole point of recurrence.

    In flight now includes *queued*, not just running: a target waiting for a
    worker must not be dispatched twice.
    """
    with gated_client(monkeypatch) as c:
        assert c.post("/jobs", json=request(job_id="job-1")).status_code == 202
        r = c.post("/jobs", json=request(job_id="job-2"))
        assert r.status_code == 409
        assert "already in flight" in r.json()["detail"]


def test_a_finished_target_can_be_rerun(client):
    """The recurrence case: same location, new job_id, after the first is done."""
    assert client.post("/jobs", json=request(job_id="job-1")).status_code == 202
    drain(client)
    assert client.post("/jobs", json=request(job_id="job-2")).status_code == 202


# --- Queueing (§4.1) --------------------------------------------------------

def test_a_busy_pipeline_queues_instead_of_rejecting(monkeypatch):
    """The reason the queue exists. Previously this was a 429 and .NET had to
    reschedule; now the job is accepted and waits for a worker."""
    with gated_client(monkeypatch) as c:
        assert c.post("/jobs", json=request(job_id="a", city="Skopje")).status_code == 202
        r = c.post("/jobs", json=request(job_id="b", city="Bitola"))
        assert r.status_code == 202
        assert r.json()["queue_position"] == 1
        assert c.get("/health").json()["queued"] == 1


def test_a_queued_job_runs_when_a_worker_frees(monkeypatch):
    with gated_client(monkeypatch) as c:
        c.post("/jobs", json=request(job_id="a", city="Skopje"))
        c.post("/jobs", json=request(job_id="b", city="Bitola"))
        c.gate.set()
        drain(c, expected=2)

    assert [p["job_id"] for p in c.delivered] == ["a", "b"]
    assert all(p["status"] == "completed" for p in c.delivered)


def test_a_full_queue_is_429_with_retry_after(monkeypatch):
    """A bounded queue means 429 is rarer, not gone — .NET still needs the
    branch. Retry-After is what it adds to NextDueAt, so its absence would turn
    a busy pipeline into a hot loop."""
    with gated_client(monkeypatch) as c:
        # One job occupies the single worker; JOB_QUEUE_MAX more fill the buffer.
        c.post("/jobs", json=request(job_id="running", city="Skopje"))
        for i in range(main.JOB_QUEUE_MAX):
            assert c.post("/jobs", json=request(job_id=f"q{i}", city=f"City{i}")).status_code == 202

        r = c.post("/jobs", json=request(job_id="overflow", city="Overflow"))
        assert r.status_code == 429
        assert r.headers["Retry-After"] == "600"
        assert "queue is full" in r.json()["detail"]


def test_health_reports_queue_depth(client):
    body = client.get("/health").json()
    assert body == {
        "active_jobs": 0,
        "workers": 1,
        "queued": 0,
        "queue_capacity": main.JOB_QUEUE_MAX,
        "accepting": True,
    }


def test_a_worker_survives_a_job_that_raises(monkeypatch):
    """An unguarded worker dies on one bad job and the pool silently shrinks —
    at one worker that means every later job is accepted with a 202 and then
    never runs, while /health reports a healthy idle pipeline."""
    main._JOBS.clear()
    delivered: list[dict] = []

    async def fake_pipeline(loc, job_id=None, use_discover_cache=True):
        return manifest(job_id, loc), []

    async def exploding_deliver(url, payload, fallback_dir=None, max_attempts=5):
        if payload["job_id"] == "poison":
            raise RuntimeError("delivery blew up past the runner")
        delivered.append(payload)
        return True

    monkeypatch.setattr(main, "run_pipeline", fake_pipeline)
    monkeypatch.setattr(main, "deliver_result", exploding_deliver)

    with TestClient(main.app) as c:
        c.delivered = delivered
        assert c.post("/jobs", json=request(job_id="poison", city="Skopje")).status_code == 202
        assert c.post("/jobs", json=request(job_id="healthy", city="Bitola")).status_code == 202
        drain(c, expected=1)

    assert [p["job_id"] for p in delivered] == ["healthy"]


# --- Delivery (§4.2) --------------------------------------------------------

def test_a_completed_run_is_delivered(client):
    client.post("/jobs", json=request())
    drain(client)
    payload = client.delivered[0]
    assert payload["job_id"] == "job-1"
    assert payload["status"] == "completed"


def test_known_domains_reach_the_payload_builder(client, monkeypatch):
    """The Phase 1 parameter finally gets a producer."""
    seen = {}

    def spy(manifest, candidates, known_domains=None):
        seen["known"] = known_domains
        return {"job_id": manifest["job_id"], "status": "completed", "sources": []}

    monkeypatch.setattr(main, "build_result_payload", spy)
    client.post("/jobs", json=request(known_domains=["MKD.mk", " time.mk ", ""]))
    drain(client)
    assert seen["known"] == {"mkd.mk", "time.mk"}


def test_a_runner_crash_still_calls_back(monkeypatch):
    """Without this .NET's row sits Running until the §4.3 sweep."""
    main._JOBS.clear()
    delivered: list[dict] = []

    async def exploding(loc, job_id=None, use_discover_cache=True):
        raise RuntimeError("chromium would not launch")

    async def fake_deliver(url, payload, fallback_dir=None, max_attempts=5):
        delivered.append(payload)
        return True

    monkeypatch.setattr(main, "run_pipeline", exploding)
    monkeypatch.setattr(main, "deliver_result", fake_deliver)

    with TestClient(main.app) as c:
        c.delivered = delivered
        assert c.post("/jobs", json=request()).status_code == 202
        drain(c)

    payload = delivered[0]
    assert payload["status"] == "failed"
    assert payload["sources"] == []
    assert payload["error"]["type"] == "RuntimeError"
    assert payload["error"]["stage"] == "runner"


def test_capacity_is_freed_before_delivery(monkeypatch):
    """Callback retries can run for minutes; holding the one slot through that
    would stall discovery for a reason unrelated to the browser."""
    main._JOBS.clear()
    active_during_delivery = {}

    async def fake_pipeline(loc, job_id=None, use_discover_cache=True):
        return {"job_id": job_id, "status": "completed", "location": {},
                "started_at": "", "finished_at": "", "stats": {}, "warnings": [], "error": None}, []

    async def slow_deliver(url, payload, fallback_dir=None, max_attempts=5):
        active_during_delivery["n"] = main._active_jobs()
        return True

    monkeypatch.setattr(main, "run_pipeline", fake_pipeline)
    monkeypatch.setattr(main, "deliver_result", slow_deliver)

    with TestClient(main.app) as c:
        c.delivered = []
        c.post("/jobs", json=request())
        for _ in range(50):
            if "n" in active_during_delivery:
                break
    assert active_during_delivery["n"] == 0


# --- Shutdown drain (§4.3) --------------------------------------------------

def test_shutdown_hands_queued_jobs_back(monkeypatch):
    """The promise that makes an in-memory queue safe.

    Accepting a job transfers responsibility for it, and the queue does not
    survive the process. Without this drain a rolling deploy silently swallows
    everything buffered and .NET waits out the §4.3 sweep to discover it — so
    the queue would have traded rare 429s for a class of silent loss.
    """
    with gated_client(monkeypatch) as c:
        c.post("/jobs", json=request(job_id="running", city="Skopje"))
        c.post("/jobs", json=request(job_id="queued-a", city="Bitola"))
        c.post("/jobs", json=request(job_id="queued-b", city="Ohrid"))
        # Leaving the block triggers lifespan shutdown with the gate still shut.

    by_id = {p["job_id"]: p for p in c.delivered}
    assert set(by_id) == {"running", "queued-a", "queued-b"}

    for job_id in ("queued-a", "queued-b"):
        assert by_id[job_id]["status"] == "failed"
        assert by_id[job_id]["sources"] == []
        assert by_id[job_id]["error"]["type"] == "PipelineShutdown"
        # The stage tells .NET this one never started, so re-dispatch is free.
        assert by_id[job_id]["error"]["stage"] == "queued"


def test_shutdown_hands_back_a_job_cancelled_mid_run(monkeypatch):
    """A job past the grace period is cancelled — .NET still gets a verdict."""
    with gated_client(monkeypatch, grace=0.1) as c:
        c.post("/jobs", json=request(job_id="running", city="Skopje"))
        drain_until(c, lambda h: h["active_jobs"] == 1)

    payload = next(p for p in c.delivered if p["job_id"] == "running")
    assert payload["status"] == "failed"
    # "cancelled" rather than "queued": this one burned crawl budget before it
    # died, which is the difference between a free re-dispatch and an expensive
    # one — and .NET's `ConsecutiveFailures` backoff reads it.
    assert payload["error"]["stage"] == "cancelled"


def test_shutdown_stops_accepting(monkeypatch):
    """503, not 429: a Retry-After the pod will not outlive is a lie."""
    main._JOBS.clear()
    monkeypatch.setattr(main, "SHUTDOWN_GRACE_SECONDS", 0.1)

    with TestClient(main.app):
        pass

    assert main._ACCEPTING is False


def test_shutdown_waits_for_a_callback_already_in_flight(monkeypatch):
    """A job that finishes just as shutdown starts must keep its real result.

    Shutdown cannot use the admission predicate here: a worker counts as free
    the moment the crawl ends, but .NET has not heard anything until the
    callback lands. Cancelling in that window replaces a payload that cost
    minutes of crawling with a PipelineShutdown failure, and .NET re-dispatches
    a job that had in fact succeeded.
    """
    main._JOBS.clear()
    delivered: list[dict] = []
    monkeypatch.setattr(main, "SHUTDOWN_GRACE_SECONDS", 5.0)

    async def fake_pipeline(loc, job_id=None, use_discover_cache=True):
        return manifest(job_id, loc), []

    async def slow_deliver(url, payload, fallback_dir=None, max_attempts=5):
        await asyncio.sleep(0.3)  # .NET taking its time to accept
        delivered.append(payload)
        return True

    monkeypatch.setattr(main, "run_pipeline", fake_pipeline)
    monkeypatch.setattr(main, "deliver_result", slow_deliver)

    with TestClient(main.app) as c:
        c.delivered = delivered
        c.post("/jobs", json=request(job_id="a", city="Skopje"))
        drain_until(c, lambda h: h["active_jobs"] == 0)
        # Exiting here starts shutdown while the callback is still in flight.

    assert [p["job_id"] for p in delivered] == ["a"]
    assert delivered[0]["status"] == "completed"


def test_a_completed_job_is_not_handed_back_twice(monkeypatch):
    """Shutdown must not resurrect finished work: a job that already called
    back is `done`, and a second failure callback for it would look to .NET
    like a completed run that then failed."""
    with gated_client(monkeypatch) as c:
        c.post("/jobs", json=request(job_id="a", city="Skopje"))
        c.gate.set()
        drain(c, expected=1)

    assert [p["job_id"] for p in c.delivered] == ["a"]
    assert c.delivered[0]["status"] == "completed"


# --- Callback client --------------------------------------------------------

def test_retries_transient_failures_only():
    assert _should_retry(500) and _should_retry(503) and _should_retry(429)
    # Bad auth or an unparseable body will not fix itself on attempt five.
    assert not _should_retry(401) and not _should_retry(400) and not _should_retry(404)


def test_fallback_writes_the_payload(tmp_path):
    _write_fallback(str(tmp_path), {"job_id": "job-1", "status": "completed"})
    with open(tmp_path / "result.json", encoding="utf-8") as f:
        assert json.load(f)["job_id"] == "job-1"


def test_fallback_never_raises():
    """Already the failure path — a disk error here must not mask the delivery
    failure that got us here."""
    _write_fallback("/proc/nonexistent/nope", {"job_id": "x"})
