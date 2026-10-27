# News Feed Ingestion

`news-feed-ingestion` is the standalone RSS/Atom ingestion worker for
`NewsAggregatorApp`. It claims feeds from the backend, uses conditional HTTP requests,
parses feed entries, downloads article pages under strict limits, extracts article text
with Trafilatura, and delivers normalized batches back to the backend.

The detailed system design is in
`../NewsAggregatorApp/docs/rss-ingestion-service.md`.

## What is implemented

- Backend-managed claim/complete/fail workflow with retry-safe completion keys.
- RSS and Atom parsing through `feedparser`.
- Conditional feed requests using ETag and Last-Modified.
- Trafilatura article text and metadata extraction.
- RSS-only fallback when an individual page cannot be fetched or extracted.
- URL normalization, tracking-parameter removal, DNS-based SSRF checks, manual redirect
  validation, content-type checks, timeouts, and response-size limits.
- Independent feed, article, and per-host concurrency limits.
- Structured JSON logs and graceful SIGINT/SIGTERM shutdown.
- A non-root Docker image and fixture-based unit/contract tests.

## Backend integration

The worker expects these internal endpoints:

```text
POST /api/internal/feed-ingestion/claims
POST /api/internal/feed-ingestion/claims/{leaseId}/complete
POST /api/internal/feed-ingestion/claims/{leaseId}/fail
```

Their JSON contract is defined by `src/news_feed_ingestion/models.py` and the architecture
document. The endpoints and the `FeedIngestionFoundation` migration are implemented in
`NewsAggregatorApp`. Configure the API and worker with the same dedicated secret:

```text
# NewsAggregatorApp
FeedIngestion__Token=replace-me
FeedIngestion__ExternalWorkerEnabled=true

# news-feed-ingestion
BACKEND_INGESTION_TOKEN=replace-me
BACKEND_BASE_URL=http://localhost:8082
```

`ExternalWorkerEnabled=true` disables the old in-process `.NET` RSS worker. Leave it `false`
until the migration has been applied and this service is running.

## Local setup

```bash
python -m venv .venv
source .venv/bin/activate
python -m pip install -e '.[dev]'
cp .env.example .env
```

Set `BACKEND_INGESTION_TOKEN` to a dedicated service token. It must not reuse the discovery
callback or development admin token.

Apply the backend migration from `NewsAggregatorApp` before the first run:

```bash
dotnet ef database update \
  --project NewsApplication.Repository \
  --startup-project NewsApplication.Web
```

## Run

```bash
news-feed-ingestion
```

The worker claims immediately. If no full batch is returned, it waits
`POLL_INTERVAL_SECONDS` before claiming again.

## Test and lint

```bash
pytest
ruff check .
```

## Docker

```bash
docker build -t news-feed-ingestion .
docker run --rm --env-file .env news-feed-ingestion
```

The container requires outbound HTTPS to public publisher sites and private network access to
the backend. Do not expose it through the public ingress.

## Important security boundary

This service fetches untrusted publisher URLs. Application-level checks reject non-public IP
destinations before each request and redirect, but deployment egress rules should also block
private, link-local, metadata, and cluster control-plane ranges. DNS validation alone cannot
fully pin the connection against DNS rebinding; production deployment should pair these checks
with network policy or an egress proxy.
