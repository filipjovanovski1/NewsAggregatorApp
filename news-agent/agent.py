"""Backward-compatible imports for code that used the old top-level module."""

from news_agent.agent import SYSTEM_PROMPT, NewsAgentRunner

__all__ = ["SYSTEM_PROMPT", "NewsAgentRunner"]
