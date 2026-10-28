import json
import unittest

from langchain_core.messages import AIMessage, SystemMessage, ToolMessage
from langgraph.errors import GraphRecursionError

from news_agent.schemas import ChatContext, ChatRequest
from news_agent.service import ChatService
from news_agent.sessions import GroundedArticle, InMemorySessionStore


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
                "description": "A useful summary of the example story.",
                "imageUrl": "https://example.com/story.jpg",
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


class DecoratedCitationRunner(FakeRunner):
    async def run(self, messages, context):
        result = await super().run(messages, context)
        result[-1] = AIMessage(content=f"The report describes an example. 【article:{ARTICLE_ID}】")
        return result


class CapturingRunner(FakeRunner):
    def __init__(self):
        self.messages = []

    async def run(self, messages, context):
        self.messages = messages
        return await super().run(messages, context)


class RecursingRunner:
    def __init__(self):
        self.calls = 0

    async def run(self, messages, context):
        self.calls += 1
        raise GraphRecursionError("tool loop")


class PreviousGroundingRunner:
    async def run(self, messages, context):
        return [*messages, AIMessage(content=f"The previous source still supports this. [article:{ARTICLE_ID}]")]


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
        self.assertEqual(response.citations[0].description, "A useful summary of the example story.")
        self.assertEqual(response.citations[0].image_url, "https://example.com/story.jpg")
        self.assertEqual(response.scope.label, "North Macedonia")

    async def test_does_not_attach_uncited_search_results(self):
        sessions = InMemorySessionStore(300, 10, 12)
        response = await ChatService(UncitedRunner(), sessions).chat(ChatRequest(
            message="What is the economic situation?",
            context=ChatContext(scopeKey="country:MK", scopeLabel="North Macedonia"),
        ))

        self.assertEqual(response.answer, "I could not find a relevant article.")
        self.assertEqual(response.citations, [])

    async def test_accepts_decorated_article_markers_from_the_model(self):
        sessions = InMemorySessionStore(300, 10, 12)
        response = await ChatService(DecoratedCitationRunner(), sessions).chat(ChatRequest(
            message="What happened?",
            context=ChatContext(scopeKey="country:MK"),
        ))

        self.assertEqual(response.answer, "The report describes an example. [1]")
        self.assertEqual([citation.article_id for citation in response.citations], [ARTICLE_ID])

    async def test_follow_up_includes_previous_grounded_article_ids(self):
        sessions = InMemorySessionStore(300, 10, 12)
        conversation = await sessions.get_or_create(None, ChatContext(scopeKey="country:MK"))
        await sessions.append(
            conversation.conversation_id,
            "What happened?",
            "An example happened. [1]",
            grounded_articles=[GroundedArticle(ARTICLE_ID, "Example story", "Example News")],
        )
        runner = CapturingRunner()

        await ChatService(runner, sessions).chat(ChatRequest(
            conversationId=conversation.conversation_id,
            message="Which sources agree?",
            context=ChatContext(scopeKey="country:MK", visibleArticleIds=[ARTICLE_ID]),
        ))

        grounded_context = [
            message.content
            for message in runner.messages
            if isinstance(message, SystemMessage) and "Grounded sources" in str(message.content)
        ]
        self.assertEqual(len(grounded_context), 1)
        self.assertIn(ARTICLE_ID, grounded_context[0])
        previous_answers = [message.content for message in runner.messages if isinstance(message, AIMessage)]
        self.assertIn(f"[article:{ARTICLE_ID}]", previous_answers[0])

    async def test_follow_up_can_cite_prior_article_without_searching_again(self):
        sessions = InMemorySessionStore(300, 10, 12)
        conversation = await sessions.get_or_create(None, ChatContext(scopeKey="country:MK"))
        await sessions.append(
            conversation.conversation_id,
            "What happened?",
            "An example happened. [1]",
            grounded_articles=[GroundedArticle(
                ARTICLE_ID,
                "Example story",
                "Example News",
                url="https://example.com/story",
                description="A useful summary.",
            )],
        )

        response = await ChatService(PreviousGroundingRunner(), sessions).chat(ChatRequest(
            conversationId=conversation.conversation_id,
            message="Does that source still support the answer?",
            context=ChatContext(scopeKey="country:MK", visibleArticleIds=[ARTICLE_ID]),
        ))

        self.assertEqual(response.answer, "The previous source still supports this. [1]")
        self.assertEqual([citation.article_id for citation in response.citations], [ARTICLE_ID])
        self.assertEqual(response.citations[0].url, "https://example.com/story")

    async def test_does_not_retry_graph_recursion_errors(self):
        sessions = InMemorySessionStore(300, 10, 12)
        runner = RecursingRunner()

        with self.assertRaises(GraphRecursionError):
            await ChatService(runner, sessions).chat(ChatRequest(message="Loop"))

        self.assertEqual(runner.calls, 1)


if __name__ == "__main__":
    unittest.main()
