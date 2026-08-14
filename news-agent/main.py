from langchain_core.messages import HumanMessage

from agent import graph

if __name__ == "__main__":
    user_query = """
    Give me all finance news in Skopje between the 1.2.2026 and 2.7.2026. Is there anything about Komercijalna Banka?
    """

    result = graph.invoke({"messages": [HumanMessage(content=user_query)]})

    for msg in result["messages"]:
        print(f"\n[{msg.type}] {msg.content}")