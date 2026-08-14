from typing import Annotated

from langchain_ollama import ChatOllama
from langgraph.graph import StateGraph, START, END
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode
from pydantic import BaseModel
from tools import resolve_scope, search_articles, search_preview


class NewsAgentState(BaseModel):
    messages: Annotated[list, add_messages]


tools = [resolve_scope, search_articles, search_preview]
llm = ChatOllama(model="gpt-oss:120b-cloud").bind_tools(tools)

def agent(state: NewsAgentState) -> NewsAgentState:
    """Call the LLM with the current messages and tool definitions."""
    response = llm.invoke(state.messages)
    return NewsAgentState(messages=[response])


def should_continue(state: NewsAgentState) -> str:
    """Route to tools if the last message has tool calls, otherwise end."""
    last_message = state.messages[-1]
    if last_message.tool_calls:
        return "tools"
    return END


graph = (
    StateGraph(NewsAgentState)
    .add_node("agent", agent)
    .add_node("tools", ToolNode(tools))
    .add_edge(START, "agent")
    .add_conditional_edges("agent", should_continue, ["tools", END])
    .add_edge("tools", "agent")
    .compile()
)