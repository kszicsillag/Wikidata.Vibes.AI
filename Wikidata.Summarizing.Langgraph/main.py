"""
Wikidata Summarizing using LangGraph with Azure OpenAI
"""

import os
import asyncio
import operator
from typing import List, Literal, Annotated, TypedDict, Dict, Any
from dotenv import load_dotenv
from azure.identity import DefaultAzureCredential
from azure.identity import get_bearer_token_provider

from langchain_openai import AzureChatOpenAI
from langchain.docstore.document import Document
from langchain_core.output_parsers import StrOutputParser
from langchain_core.prompts import ChatPromptTemplate
from langchain.prompts import PromptTemplate
from langchain.chains.combine_documents.reduce import (
    acollapse_docs,
    split_list_of_docs,
)
from langgraph.graph import StateGraph, END, START
from langgraph.constants import Send

# Load environment variables
load_dotenv()

# Configuration
token_max = 1000

# Azure OpenAI configuration using DefaultAzureCredential for Entra authentication
AZURE_OPENAI_ENDPOINT = os.getenv("AZURE_OPENAI_ENDPOINT")
AZURE_OPENAI_DEPLOYMENT_NAME = os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME")
AZURE_OPENAI_API_VERSION = os.getenv("AZURE_OPENAI_API_VERSION", "2024-02-15-preview")

if not all([AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT_NAME]):
    raise ValueError("Azure OpenAI environment variables are not set")

# Create credential using DefaultAzureCredential
credential = DefaultAzureCredential()

az_open_api_key = credential.get_token("https://cognitiveservices.azure.com/.default").token

# Initialize the LLM with azure_ad_token_provider
llm = AzureChatOpenAI(
    api_key=az_open_api_key,
    azure_endpoint=AZURE_OPENAI_ENDPOINT,
    deployment_name=AZURE_OPENAI_DEPLOYMENT_NAME,
    api_version=AZURE_OPENAI_API_VERSION,
    temperature=0.7,
    tiktoken_model_name="gpt-4o" #needed due to https://github.com/openai/tiktoken/issues/395
)

map_template = """
Write a summary for a text input.  
The input is a subset of statements about a knowledge base entity, one statement per line. 
One statment starts with predicate followed by object, then metadata about the statment.
Use only the statements that are relevent for entity alignment for other knowledge bases.
If you cannot find such statement, return empty text.
If you can find statement, write a concise, factual summary. 
All statements in the summary must be backed up by the input.   
Text input: {context}.
"""

reduce_template = """
Take the input text as a set of summaries and distill it into a final, consolidated summary.
All summaries are about the same knowledge base entity.
Optimize the final summary for knowledge base alignement. 
Do not add additional statements or claims to the final summary.
{docs}
"""

map_prompt = ChatPromptTemplate([("human", map_template)])
reduce_prompt = ChatPromptTemplate([("human", reduce_template)])

reduce_chain = reduce_prompt | llm | StrOutputParser()
map_chain = map_prompt | llm | StrOutputParser()

# This will be the overall state of the main graph.
# It will contain the input document contents, corresponding
# summaries, and a final summary.
class OverallState(TypedDict):
    # Notice here we use the operator.add
    # This is because we want combine all the summaries we generate
    # from individual nodes back into one list - this is essentially
    # the "reduce" part
    contents: List[str]
    summaries: Annotated[list, operator.add]
    collapsed_summaries: List[Document]
    final_summary: str

# This will be the state of the node that we will "map" all
# documents to in order to generate summaries
class SummaryState(TypedDict):
    content: str

def length_function_str_list(documents: List[str]) -> int:
    """Get number of tokens for input list of strings."""
    if hasattr(llm, "get_num_tokens"):
        return sum(llm.get_num_tokens(doc) for doc in documents)
    elif hasattr(llm, "_llm") and hasattr(llm._llm, "get_num_tokens"):
        return sum(llm._llm.get_num_tokens(doc) for doc in documents)
    else:
        # Fallback: approximate token count by word count
        return sum(len(doc.split()) for doc in documents)

def length_function(documents: List[Document]) -> int:
    """Get number of tokens for input contents."""
    str_list = [doc.page_content for doc in documents]
    return length_function_str_list(str_list)

def split_str(document: str, max_tokens: int = token_max) -> List[str]:
    """
    Recursively split the input string if its token count exceeds max_tokens.
    Splitting is done at the next line break after the median character.
    Returns a list of strings where each string's token count is <= max_tokens.
    """
    if length_function_str_list([document]) <= max_tokens:
        return [document]

    median_index = len(document) // 2
    split_index = document.find('\n', median_index)
    if split_index == -1:
        # No line break found after median, try before median
        split_index = document.rfind('\n', 0, median_index)
        if split_index == -1:
            # No line break at all, fallback to splitting at median
            split_index = median_index

    left_part = document[:split_index]
    right_part = document[split_index:]

    left_splits = split_str(left_part, max_tokens)
    right_splits = split_str(right_part, max_tokens)

    return left_splits + right_splits

def collect_summaries(state: OverallState) -> Dict[str, Any]:
    return {
        "collapsed_summaries": [Document(summary) for summary in state["summaries"]]
    }

# Modify final summary to read off collapsed summaries
async def generate_final_summary(state: OverallState) -> Dict[str, str]:
    response = await reduce_chain.ainvoke(state["collapsed_summaries"])
    return {"final_summary": response}

# Add node to collapse summaries
async def collapse_summaries(state: OverallState) -> Dict[str, List[Document]]:
    doc_lists = split_list_of_docs(
        state["collapsed_summaries"], length_function, token_max
    )
    results: List[Document] = []
    for doc_list in doc_lists:
        results.append(await acollapse_docs(doc_list, reduce_chain.ainvoke))

    return {"collapsed_summaries": results}

def should_collapse(
    state: OverallState,
) -> Literal["collapse_summaries", "generate_final_summary"]:
    num_tokens = length_function(state["collapsed_summaries"])
    if num_tokens > token_max:
        return "collapse_summaries"
    else:
        return "generate_final_summary"

# Here we generate a summary, given a document
async def generate_summary(state: SummaryState) -> Dict[str, List[str]]:
    response = await map_chain.ainvoke(state["content"])
    return {"summaries": [response]}

# Here we define the logic to map out over the documents
# We will use this an edge in the graph
def map_summaries(state: OverallState) -> List[Send]:
    # We will return a list of `Send` objects
    # Each `Send` object consists of the name of a node in the graph
    # as well as the state to send to that node
    return [
        Send("generate_summary", {"content": content}) for content in state["contents"]
    ]

def create_graph() -> StateGraph:
    # Create the graph with the new state type
    graph = StateGraph(OverallState)
    
    # Add nodes with plain async functions directly
    graph.add_node("generate_summary", generate_summary)
    graph.add_node("collect_summaries", collect_summaries)
    graph.add_node("generate_final_summary", generate_final_summary)
    graph.add_node("collapse_summaries", collapse_summaries)
    
    graph.add_conditional_edges(START, map_summaries, ["generate_summary"])
    graph.add_edge("generate_summary", "collect_summaries")
    graph.add_conditional_edges("collect_summaries", should_collapse)
    graph.add_conditional_edges("collapse_summaries", should_collapse)
    graph.add_edge("generate_final_summary", END)
    
    return graph.compile()

async def main() -> None:
    # Simple test to check Azure OpenAI connection using llm.invoke with message format
    #print("Testing Azure OpenAI connection with llm.ainvoke...")
    #ai_msg = await llm.ainvoke([
    #    ["system", "You are a helpful assistant that translates English to French. Translate the user sentence."],
    #    ["human", "I love programming."]
    #])
    #print("AI message:", ai_msg.content)

    # Test fetch_wikidata_entity with Q42 and print result
    wikidata_result = await fetch_wikidata_entity("Q42")
    split_wikidata_result = split_str(wikidata_result)
    print("Split Wikidata entity Q42 result into chunks:")
    for i, chunk in enumerate(split_wikidata_result):
        print(f"Chunk {i+1}:\n{chunk}\n")

    
    # Create the graph
    app = create_graph()
    
    # Example usage
    initial_state: OverallState = {
        "contents": split_wikidata_result,
        "summaries": [],
        "collapsed_summaries": [],
        "final_summary": ""
    }
    
    # Run the graph
    result = await app.ainvoke(initial_state)
    print("Final summary:", result["final_summary"])
    

import aiohttp

async def fetch_wikidata_entity(wikidata_id: str) -> str:
    """
    Fetch the content of the Wikidata entity by sending a SPARQL query to the Wikidata SPARQL endpoint.
    The query is built using a simple string template and returns results in CSV format.
    Args:
        wikidata_id (str): The Wikidata entity ID (e.g., Q42).
    Returns:
        str: The CSV content of the SPARQL query results.
    """
    sparql_query = f'''
    PREFIX wd: <http://www.wikidata.org/entity/>
    PREFIX wikibase: <http://wikiba.se/ontology#>
    PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
    PREFIX bd: <http://www.bigdata.com/rdf#>

    SELECT 
    #?property 
    ?propertyLabel 
    #?statementValue 
    ?statementValueLabel 
    #?qualifierProperty 
    ?qualifierPropertyLabel 
    #?qualifierValue 
    ?qualifierValueLabel 
    ?statementRankLabel 
    WHERE {{
      wd:{wikidata_id} ?propertyPredicate ?statement .
      ?statement ?statementPropertyPredicate ?statementValue .
      ?property wikibase:claim ?propertyPredicate .
      ?property wikibase:statementProperty ?statementPropertyPredicate .
      ?statement wikibase:rank ?statementRank .
      BIND(
            IF(?statementRank = wikibase:NormalRank, "",
                IF(?statementRank = wikibase:PreferredRank, "Preferred statement",
                    IF(?statementRank = wikibase:DeprecatedRank, "Deprecated statment", "")
                   )
            ) AS ?statementRankLabel
       )
      OPTIONAL {{
        ?statement ?qualifierPredicate ?qualifierValue .
        ?qualifierProperty wikibase:qualifier ?qualifierPredicate .
      }}
      ?property wikibase:propertyType ?propertyType .
      FILTER(?propertyType != wikibase:ExternalId && ?propertyType != wikibase:CommonsMedia && ?propertyType != wikibase:Url)
      SERVICE wikibase:label {{ bd:serviceParam wikibase:language "en" . }}
    }}
    ORDER BY ?property ?statementValue ?qualifierProperty ?qualifierValue
    '''

    url = "https://query.wikidata.org/sparql"
    headers = {
        "Accept": "text/csv",
        "Content-Type": "application/sparql-query"
    }
    async with aiohttp.ClientSession() as session:
        async with session.post(url, headers=headers, data=sparql_query) as response:
            response.raise_for_status()
            return await response.text()

if __name__ == "__main__":
    asyncio.run(main())
