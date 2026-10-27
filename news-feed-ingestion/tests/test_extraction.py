from pathlib import Path

import pytest

from news_feed_ingestion.extraction import extract_article

FIXTURES = Path(__file__).parent / "fixtures"


@pytest.mark.asyncio
async def test_extracts_cyrillic_article_text() -> None:
    article = await extract_article(
        (FIXTURES / "article.html").read_bytes(),
        url="https://news.example.com/story/123",
        favor_precision=True,
    )

    assert article.text is not None
    assert "првиот пасус" in article.text
    assert "вторите пасус" not in article.text
    assert article.author == "Page Author"
