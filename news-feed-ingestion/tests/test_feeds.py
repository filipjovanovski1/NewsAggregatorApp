from datetime import UTC, datetime
from pathlib import Path

from news_feed_ingestion.feeds import parse_feed

FIXTURES = Path(__file__).parent / "fixtures"


def test_parse_rss_entry_and_normalize_url() -> None:
    entries = parse_feed(
        (FIXTURES / "feed.xml").read_bytes(),
        max_entries=10,
        fallback_time=datetime(2026, 10, 23, tzinfo=UTC),
    )

    assert len(entries) == 1
    article = entries[0]
    assert article.external_id == "story-123"
    assert article.url == "https://news.example.com/story/123"
    assert article.title == "Нова приказна од Скопје"
    assert article.summary == "Краток опис на веста."
    assert article.categories == ["local"]
    assert article.published_at == datetime(2026, 10, 23, 10, 30, tzinfo=UTC)
