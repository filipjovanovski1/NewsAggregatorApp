import unittest

from news_agent.schemas import ChatContext
from news_agent.sessions import GroundedArticle, InMemorySessionStore


class SessionStoreTests(unittest.IsolatedAsyncioTestCase):
    async def test_history_is_bounded_and_context_is_retained(self):
        store = InMemorySessionStore(ttl_seconds=300, max_sessions=10, max_history_messages=2)
        session = await store.get_or_create(None, ChatContext(scopeKey="country:MK"))

        await store.append(session.conversation_id, "one", "answer one")
        await store.append(session.conversation_id, "two", "answer two")
        loaded = await store.get_or_create(session.conversation_id, None)

        self.assertEqual([turn.content for turn in loaded.turns], ["two", "answer two"])
        self.assertEqual(loaded.context.scope_key, "country:MK")

    async def test_retains_grounded_articles_with_the_assistant_turn(self):
        store = InMemorySessionStore(ttl_seconds=300, max_sessions=10, max_history_messages=4)
        session = await store.get_or_create(None, ChatContext(scopeKey="country:MK"))

        await store.append(
            session.conversation_id,
            "What happened?",
            "An example happened. [1]",
            grounded_articles=[GroundedArticle("article-1", "Example", "Example News")],
        )

        loaded = await store.get_or_create(session.conversation_id, None)
        self.assertEqual(loaded.turns[-1].grounded_articles[0].article_id, "article-1")


if __name__ == "__main__":
    unittest.main()
