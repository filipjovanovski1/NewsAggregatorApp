from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


def _to_camel(value: str) -> str:
    first, *rest = value.split("_")
    return first + "".join(part.capitalize() for part in rest)


class ApiModel(BaseModel):
    model_config = ConfigDict(alias_generator=_to_camel, populate_by_name=True)


class ClaimRequest(ApiModel):
    worker_id: str
    max_items: int = Field(gt=0)
    lease_seconds: int = Field(gt=0)


class FeedClaim(ApiModel):
    lease_id: UUID
    lease_version: int
    lease_expires_at: datetime
    feed_id: UUID
    feed_url: str
    source_id: UUID
    source_domain: str
    source_name: str | None = None
    language: str | None = None
    etag: str | None = None
    last_modified: str | None = None
    last_polled_at: datetime | None = None
    polling_tier: str | None = None


class ClaimResponse(ApiModel):
    claims: list[FeedClaim] = Field(default_factory=list)


ExtractionStatus = Literal["extracted", "rss_only", "failed", "blocked"]


class ArticlePayload(ApiModel):
    external_entry_id: str
    entry_url: str
    canonical_url: str
    title: str
    description: str | None = None
    content_text: str | None = None
    author: str | None = None
    language: str | None = None
    image_url: str | None = None
    published_at: datetime
    modified_at: datetime | None = None
    categories: list[str] = Field(default_factory=list)
    content_hash: str | None = None
    extraction_status: ExtractionStatus
    extractor: str | None = None
    extractor_version: str | None = None


class FeedPollMetadata(ApiModel):
    requested_url: str
    final_url: str
    http_status: int
    etag: str | None = None
    last_modified: str | None = None
    polled_at: datetime


class CompleteRequest(ApiModel):
    lease_version: int
    feed: FeedPollMetadata
    articles: list[ArticlePayload] = Field(default_factory=list)


FailureStage = Literal[
    "feed_fetch",
    "feed_parse",
    "article_fetch",
    "article_extract",
    "backend_delivery",
    "unexpected",
]


class FailureRequest(ApiModel):
    lease_version: int
    failed_at: datetime
    stage: FailureStage
    error_type: str
    message: str
    http_status: int | None = None
    retry_after_seconds: int | None = None
