# Wikidata.AlignWithMCP.SemanticKernel - Developer Setup Guide

This project integrates Semantic Kernel with a local MCP (Model Context Protocol) server for advanced LLM and knowledge base orchestration. Follow these steps to configure your environment as a new developer.

## Configuration Overview

All runtime settings are managed via `appsettings.json` and user secrets. The most important settings are:

- **GitHubModels:ApiKey**: Your API key for accessing GitHub Models (LLM endpoints)
- **McpServer:Path**: The path to your local MCP server Python file (e.g., `server.py`).
  - This project uses [mcp-wikidata](https://github.com/zzaebok/mcp-wikidata) as the recommended MCP server implementation.
  - Clone the MCP server from: https://github.com/zzaebok/mcp-wikidata
  - Set the path to the `server.py` file inside your local clone
- **McpServer:WorkingDirectory**: The working directory for the MCP server process

## Example `appsettings.json`

```
{
  "GitHubModels": {
    "ApiKey": "your-github-models-api-key"
  },
  "McpServer": {
    "Path": "X:\\Source\\mcp-wikidata\\src\\server.py",
    "WorkingDirectory": "X:\\Source\\mcp-wikidata\\"
  }
}
```

> **Note:** You can override these values locally using [user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

## Setting Up User Secrets (Recommended for Local Dev)

1. Run the following command in the project directory:
   ```sh
   dotnet user-secrets set "GitHubModels:ApiKey" "your-github-models-api-key"
   dotnet user-secrets set "McpServer:Path" "C:\\Source\\mcp-wikidata\\src\\server.py"
   dotnet user-secrets set "McpServer:WorkingDirectory" "C:\\Source\\mcp-wikidata\\"
   ```
2. These secrets will override values in `appsettings.json` for your user only.

## How the Settings Are Used

- The API key is required to call LLM endpoints (e.g., GitHub Models, Azure OpenAI)
- The MCP server path and working directory are used to launch the local Python server that handles SPARQL queries to Wikidata
- All configuration is validated at startup; missing or invalid settings will cause the app to exit with an error message

## Troubleshooting

- If you see errors about missing configuration, check your `appsettings.json` and user secrets
- Make sure the MCP server path and working directory are correct and accessible
- Ensure your API key is valid and has access to the required LLM endpoints

## Additional Resources

- [Semantic Kernel Documentation](https://github.com/microsoft/semantic-kernel)
- [Model Context Protocol (MCP)](https://github.com/contextprotocol/model-context-protocol)
- [User Secrets in .NET](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
---
