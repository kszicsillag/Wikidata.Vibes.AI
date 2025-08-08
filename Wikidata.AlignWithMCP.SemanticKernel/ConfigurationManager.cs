using Microsoft.Extensions.Configuration;

namespace Wikidata.AlignWithMCP.SemanticKernel;

public class AppConfiguration
{
    public required string GitHubModelsApiKey { get; init; }
    public required string WikidataMcpPath { get; init; }
    public required string WikidataMcpWorkingDirectory { get; init; }

    public static AppConfiguration CreateFromConfiguration(IConfiguration configuration)
    {
        var errors = new List<string>();

        var apiKey = configuration["GitHubModels:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            errors.Add("GitHub Models API key not found. Please set 'GitHubModels:ApiKey' in user-secrets or environment variables.");
        }

        var mcpPath = configuration["McpServer:Path"];
        if (string.IsNullOrEmpty(mcpPath))
        {
            errors.Add("MCP server path not found. Please set 'McpServer:Path' in appsettings.json, user-secrets, or environment variables.");
        }

        var workingDirectory = configuration["McpServer:WorkingDirectory"];
        if (string.IsNullOrEmpty(workingDirectory))
        {
            errors.Add("MCP server working directory not found. Please set 'McpServer:WorkingDirectory' in appsettings.json, user-secrets, or environment variables.");
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            Environment.Exit(1);
        }

        return new AppConfiguration
        {
            GitHubModelsApiKey = apiKey!,
            WikidataMcpPath = mcpPath!,
            WikidataMcpWorkingDirectory = workingDirectory!
        };
    }
}

public static class ConfigurationSetup
{
    public static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables();
        
        return builder.Build();
    }

    public static AppConfiguration LoadAndValidateConfiguration(IConfiguration configuration)
    {
        return AppConfiguration.CreateFromConfiguration(configuration);
    }
}
