"""Backward-compatible imports for the agent's controlled backend tools."""

from news_agent.backend import BackendClient
from news_agent.tools import build_tools

__all__ = ["BackendClient", "build_tools"]
