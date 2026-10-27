from __future__ import annotations

import socket

from pydantic import AnyHttpUrl, Field, PositiveInt, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Runtime settings loaded from environment variables or a local .env file."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        case_sensitive=False,
        populate_by_name=True,
    )

    backend_base_url: AnyHttpUrl = Field(validation_alias="BACKEND_BASE_URL")
    backend_ingestion_token: SecretStr = Field(validation_alias="BACKEND_INGESTION_TOKEN")
    worker_id: str = Field(default_factory=socket.gethostname, validation_alias="WORKER_ID")

    claim_batch_size: PositiveInt = Field(default=5, validation_alias="CLAIM_BATCH_SIZE")
    lease_seconds: PositiveInt = Field(default=300, validation_alias="LEASE_SECONDS")
    poll_interval_seconds: float = Field(default=10, gt=0, validation_alias="POLL_INTERVAL_SECONDS")

    max_concurrent_feeds: PositiveInt = Field(default=5, validation_alias="MAX_CONCURRENT_FEEDS")
    max_concurrent_articles: PositiveInt = Field(
        default=20, validation_alias="MAX_CONCURRENT_ARTICLES"
    )
    max_requests_per_host: PositiveInt = Field(
        default=2, validation_alias="MAX_REQUESTS_PER_HOST"
    )
    feed_timeout_seconds: float = Field(default=15, gt=0, validation_alias="FEED_TIMEOUT_SECONDS")
    article_timeout_seconds: float = Field(
        default=20, gt=0, validation_alias="ARTICLE_TIMEOUT_SECONDS"
    )
    feed_max_bytes: PositiveInt = Field(default=2 * 1024 * 1024, validation_alias="FEED_MAX_BYTES")
    article_max_bytes: PositiveInt = Field(
        default=5 * 1024 * 1024, validation_alias="ARTICLE_MAX_BYTES"
    )
    max_articles_per_feed: PositiveInt = Field(
        default=50, validation_alias="MAX_ARTICLES_PER_FEED"
    )
    max_redirects: int = Field(default=5, ge=0, le=10, validation_alias="MAX_REDIRECTS")

    trafilatura_favor_precision: bool = Field(
        default=True, validation_alias="TRAFILATURA_FAVOR_PRECISION"
    )
    user_agent: str = Field(
        default="NewsFeedIngestion/0.1 (+https://newsaggregatorapp.local/contact)",
        validation_alias="USER_AGENT",
    )
    log_level: str = Field(default="INFO", validation_alias="LOG_LEVEL")
