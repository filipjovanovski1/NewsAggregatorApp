import json
import unittest

from langchain_core.messages import AIMessage, ToolMessage

from news_agent.schemas import ChatContext, ChatRequest
from news_agent.service import ChatService
from news_agent.sessions import InMemorySessionStore


ARTICLE_ID = "172d8118-477e-4fbf-83ca-920b9219d3c7"


class FakeRunner:
    async def run(self, messages, context):
        payload = {
            "scopeKey": "country:MK",
            "scopeLabel": "MK",
            "items": [{
                "articleId": ARTICLE_ID,
                "title": "Example story",
                "publisher": "Example News",
                "url": "https://example.com/story",
                "publishedTime": "2026-10-23T13:10:47Z",
            }],
        }
        return [
            *messages,
            ToolMessage(content=json.dumps(payload), name="search_articles", tool_call_id="tool-1"),
            AIMessage(content=f"The report describes an example. [article:{ARTICLE_ID}]"),
        ]


class UncitedRunner:
    async def run(self, messages, context):
        payload = {
            "scopeKey": "country:MK",
            "scopeLabel": "MK",
            "items": [{
                "articleId": ARTICLE_ID,
                "title": "Unrelated story",
                "publisher": "Example News",
                "url": "https://example.com/unrelated",
            }],
        }
        return [
            *messages,
            ToolMessage(content=json.dumps(payload), name="search_articles", tool_call_id="tool-2"),
            AIMessage(content="I could not find a relevant article."),
        ]


class ChatServiceTests(unittest.IsolatedAsyncioTestCase):
    async def test_validates_and_numbers_article_citations(self):
        sessions = InMemorySessionStore(300, 10, 12)
        response = await ChatService(FakeRunner(), sessions).chat(ChatRequest(
            message="What happened?",
            context=ChatContext(scopeKey="country:MK", scopeLabel="North Macedonia"),
        ))

        self.assertEqual(response.answer, "The report describes an example. [1]")
        self.assertEqual(len(response.citations), 1)
        self.assertEqual(response.citations[0].article_id, ARTICLE_ID)
        self.assertEqual(response.scope.label, "North Macedonia")

    async def test_does_not_attach_uncited_search_results(self):
        sessions = InMemorySessionStore(300, 10, 12)
        response = await ChatService(UncitedRunner(), sessions).chat(ChatRequest(
            message="What is the economic situation?",
            context=ChatContext(scopeKey="country:MK", scopeLabel="North Macedonia"),
        ))

        self.assertEqual(response.answer, "I could not find a relevant article.")
        self.assertEqual(response.citations, [])


if __name__ == "__main__":
    unittest.main()
