import os
from dotenv import load_dotenv
from langchain_core.messages import AnyMessage
from langchain_mcp_adapters.client import MultiServerMCPClient
from langgraph.prebuilt import create_react_agent
from langgraph.graph import StateGraph
from typing import Annotated
from typing_extensions import TypedDict
from langgraph.graph.message import add_messages
from langchain_azure_ai.chat_models import AzureAIChatCompletionsModel
import asyncio

# Load environment variables
load_dotenv()

CHATMODEL_ENDPOINT = os.getenv("CHATMODEL_ENDPOINT")
CHATMODEL_MODEL_NAME= os.getenv("CHATMODEL_MODEL_NAME")
CHATMODEL_KEY = os.getenv("CHATMODEL_KEY")

print(CHATMODEL_ENDPOINT)
print(CHATMODEL_MODEL_NAME)

if not all([CHATMODEL_ENDPOINT, CHATMODEL_MODEL_NAME, CHATMODEL_KEY]):
    raise ValueError("Chat model config variables are not set")

WIKIDATA_MCP_SERVER_STDIO_PATH = os.getenv("WIKIDATA_MCP_SERVER_STDIO_PATH")
if not all([WIKIDATA_MCP_SERVER_STDIO_PATH]):
    raise ValueError("MCP environment variables are not set")

llm = AzureAIChatCompletionsModel(
    credential=CHATMODEL_KEY,
    endpoint=CHATMODEL_ENDPOINT,
    model=CHATMODEL_MODEL_NAME
)

class State(TypedDict):
    messages: Annotated[list[AnyMessage], add_messages]

mcp_servers = {
    "wikidata": {
        "command": "python",
        # Make sure to update to the full absolute path to your math_server.py file
        "args": [WIKIDATA_MCP_SERVER_STDIO_PATH],
        "transport": "stdio"
    }
}

async def main():
    client = MultiServerMCPClient(mcp_servers)
    tools = await client.get_tools()
    agent = create_react_agent(llm, tools)

    ## Build the LangGraph StateGraph
    #graph_builder = StateGraph(State)
    #
    #def chatbot(state: State):
    #    # Use the agent with tools for each turn
    #    return {"messages": [agent.invoke(state["messages"])]}
    #
    #graph_builder.add_node("chatbot", chatbot)
    #graph_builder.set_entry_point("chatbot")
    #graph_builder.set_finish_point("chatbot")
    #graph = graph_builder.compile()
    #
    ## CLI loop
    #while True:
    #    user_input = input("User: ")
    #    if user_input.lower() in ["quit", "exit", "q"]:
    #        print("Goodbye!")
    #        break
    #    for chunk in graph.stream({"messages": [{"role": "user", "content": user_input}]}, stream_mode="updates"):
    #        print("Assistant:", chunk)
    response = await agent.ainvoke(
        {"messages": [{"role": "user", "content": "What is the capital of Hungary (Q28) according to Wikidata?"}]}
    )
    print(response)
       
# To run the async main in a script:
if __name__ == "__main__":
    asyncio.run(main())

