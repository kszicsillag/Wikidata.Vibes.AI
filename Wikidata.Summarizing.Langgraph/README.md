# Wikidata Summarizing with LangGraph

This project uses LangGraph to create a document summarization pipeline for Wikidata content, powered by Azure OpenAI.

## Setup

1. Create a virtual environment:
```bash
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate
```

2. Install dependencies:
```bash
pip install -r requirements.txt
```

3. Create a `.env` file in the project root with your Azure OpenAI configuration:
```
AZURE_OPENAI_API_KEY=your_api_key_here
AZURE_OPENAI_ENDPOINT=your_endpoint_here
AZURE_OPENAI_DEPLOYMENT_NAME=your_deployment_name
AZURE_OPENAI_API_VERSION=2024-02-15-preview  # Optional, defaults to this version
```

## Project Structure

```
.
├── README.md
├── requirements.txt
└── main.py
```

## Usage

Run the main script:
```bash
python main.py
```

## Features

- Document summarization using LangGraph
- Azure OpenAI integration
- Configurable token limits
- Async support
- Environment-based configuration

## Dependencies

- langgraph>=0.4.0
- langchain>=0.1.0
- langchain-openai>=0.0.2
- langchain-community>=0.0.10
- pydantic>=2.5.0
- typing-extensions>=4.8.0
- python-dotenv>=1.0.0 