import unittest

from langchain_core.messages import AIMessage, ToolMessage

from news_agent.agent import _tool_guard_reason


def tool_call(name: str, call_id: str, **args):
    return {"name": name, "args": args, "id": call_id, "type": "tool_call"}


class ToolGuardTests(unittest.TestCase):
    def test_blocks_an_identical_tool_call(self):
        messages = [
            AIMessage(content="", tool_calls=[tool_call("search_articles", "one", scope_key="country:MK", query="")]),
            ToolMessage(content='{"items": []}', name="search_articles", tool_call_id="one"),
            AIMessage(content="", tool_calls=[tool_call("search_articles", "two", scope_key="country:MK", query="")]),
        ]

        self.assertIn("Duplicate", _tool_guard_reason(messages))

    def test_allows_one_topic_search_and_one_fallback(self):
        messages = [
            AIMessage(content="", tool_calls=[tool_call("search_articles", "one", scope_key="country:MK", query="economy")]),
            ToolMessage(content='{"items": []}', name="search_articles", tool_call_id="one"),
            AIMessage(content="", tool_calls=[tool_call("search_articles", "two", scope_key="country:MK", query="")]),
        ]

        self.assertIsNone(_tool_guard_reason(messages))

    def test_blocks_a_third_distinct_search(self):
        messages = [
            AIMessage(content="", tool_calls=[tool_call("search_articles", "one", scope_key="country:MK", query="economy")]),
            AIMessage(content="", tool_calls=[tool_call("search_articles", "two", scope_key="country:MK", query="business")]),
            AIMessage(content="", tool_calls=[tool_call("search_articles", "three", scope_key="country:MK", query="finance")]),
        ]

        self.assertIn("budget", _tool_guard_reason(messages).lower())


if __name__ == "__main__":
    unittest.main()
