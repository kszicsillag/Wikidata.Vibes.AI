import os
from dotenv import load_dotenv
from langchain_core.messages import AnyMessage
from langchain.chat_models import init_chat_model
from langchain_mcp_adapters.client import MultiServerMCPClient
from langchain_mcp_adapters.tools import load_mcp_tools
from langgraph.prebuilt import create_react_agent
from langgraph.graph import StateGraph
from typing import Annotated
from typing_extensions import TypedDict
from langgraph.graph.message import add_messages
from langchain_openai import AzureChatOpenAI
from azure.identity import DefaultAzureCredential
import asyncio

# Load environment variables
load_dotenv()

# Azure OpenAI configuration using DefaultAzureCredential for Entra authentication
AZURE_OPENAI_ENDPOINT = os.getenv("AZURE_OPENAI_ENDPOINT")
AZURE_OPENAI_DEPLOYMENT_NAME = os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME")
AZURE_OPENAI_API_VERSION = os.getenv("AZURE_OPENAI_API_VERSION", "2024-02-15-preview")

if not all([AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT_NAME]):
    raise ValueError("Azure OpenAI environment variables are not set")

WIKIDATA_MCP_SERVER_STDIO_PATH = os.getenv("WIKIDATA_MCP_SERVER_STDIO_PATH")
if not all([WIKIDATA_MCP_SERVER_STDIO_PATH]):
    raise ValueError("MCP environment variables are not set")

# Create credential using DefaultAzureCredential
credential = DefaultAzureCredential()

az_open_api_key = credential.get_token("https://cognitiveservices.azure.com/.default").token

llm = AzureChatOpenAI(
    api_key=az_open_api_key,
    azure_endpoint=AZURE_OPENAI_ENDPOINT,
    deployment_name=AZURE_OPENAI_DEPLOYMENT_NAME,
    api_version=AZURE_OPENAI_API_VERSION,
    temperature=0.7,
    tiktoken_model_name="gpt-4o" #needed due to https://github.com/openai/tiktoken/issues/395
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
        {"messages": [{"role": "user", "content": "What is the capital of Hungary according to Wikidata?"}]}
    )
    print(response)
       
# To run the async main in a script:
if __name__ == "__main__":
    asyncio.run(main())

