using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Agents;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.SemanticKernel.ChatCompletion;

// Build configuration to enable secret management
var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();
var configuration = builder.Build();

// Get the GitHub Models API key from secrets or environment variables
var githubModelsApiKey = configuration["GitHubModels:ApiKey"];
if (string.IsNullOrEmpty(githubModelsApiKey))
{
    Console.WriteLine("GitHub Models API key not found. Please set 'GitHubModels:ApiKey' in user-secrets or environment variables.");
    return;
}

var wikidataMcpPath = configuration["McpServer:Path"];
var wikidataMcpWorkingDir = configuration["McpServer:WorkingDirectory"];
if (string.IsNullOrEmpty(wikidataMcpPath))
{
    Console.WriteLine("MCP server path not found. Please set 'McpServer:Path' in appsettings.json, user-secrets, or environment variables.");
    return;
}
if (string.IsNullOrEmpty(wikidataMcpWorkingDir))
{
    Console.WriteLine("MCP server working directory not found. Please set 'McpServer:WorkingDirectory' in appsettings.json, user-secrets, or environment variables.");
    return;
}

const string WikidataMcpName = "WikidataMCP";

await using IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = WikidataMcpName,
        WorkingDirectory = wikidataMcpWorkingDir,
        Command = "uv",
        Arguments = ["run", wikidataMcpPath],
    }));

var tools = await mcpClient.ListToolsAsync();

// Retrieve the list of tools available on the GitHub server
/*
foreach (var tool in tools)
{
    Console.WriteLine($"{tool.Name} ({tool.Description})");
}*/

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService("Wikidata.AlignWithMCP.SemanticKernel");

// Enable model diagnostics with sensitive data.
AppContext.SetSwitch("Microsoft.SemanticKernel.Experimental.GenAI.EnableOTelDiagnosticsSensitive", true);

using var traceProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource("Microsoft.SemanticKernel*")
    .AddConsoleExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter("Microsoft.SemanticKernel*")
    .AddConsoleExporter()
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    // Add OpenTelemetry as a logging provider
    builder.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resourceBuilder);
        options.AddConsoleExporter();
        // Format log messages. This is default to false.
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    });
    builder.SetMinimumLevel(LogLevel.Information);
});

// Set up the OpenAI connector for Semantic Kernel to use GitHub Models API
IKernelBuilder kbuilder = Kernel.CreateBuilder();
kbuilder.Services.AddSingleton(loggerFactory);
var kernel = kbuilder.AddOpenAIChatCompletion(
        modelId: "gpt-4.1-mini",
        apiKey: githubModelsApiKey,
        endpoint: new Uri("https://models.github.ai/inference")
    )
    .Build();

#pragma warning disable SKEXP0001
kernel.Plugins.AddFromFunctions(WikidataMcpName, tools.Select(aiFunction => aiFunction.AsKernelFunction()));

// Create ChatCompletionAgent with ReAct style instructions
var agent = new ChatCompletionAgent()
{
    Instructions = @"You are a helpful assistant that uses a ReAct (Reasoning and Acting) approach to answer questions.

When answering questions, follow this pattern:
1. **Thought**: Think about what you need to do to answer the question
2. **Action**: Use available tools to gather information
3. **Observation**: Analyze the results from the tools
4. **Thought**: Continue reasoning based on the observations
5. **Action**: Take additional actions if needed
6. **Final Answer**: Provide a comprehensive answer based on your reasoning and observations

You get your factual data exclusively from external knowledge bases using the available tools. Always use tools to verify information rather than relying on your training data.

For Wikidata queries:
- Think about what specific information you need
- Use the available Wikidata tools to query for that information
- Observe and analyze the results
- Provide a clear, factual answer based on the tool results",
    Name = "WikidataReActAgent",
    Kernel = kernel,
    Arguments = new KernelArguments(new OpenAIPromptExecutionSettings()
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    })
};
#pragma warning restore SKEXP0001

// Example: Send a prompt to the model
var prompt = "What is the capital of Hungary according to Wikidata?";
var maxRetries = 5;
for (int attempt = 0; attempt < maxRetries; attempt++)
{
    try
    {
        #pragma warning disable SKEXP0001
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(prompt);
        
        await foreach (var responseItem in agent.InvokeAsync(chatHistory))
        {
            Console.WriteLine($"Agent response: {responseItem.Message}");
        }
        #pragma warning restore SKEXP0001
        break;
    }
    catch (HttpOperationException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
    {
        // Try to get the Retry-After header from the exception's Data (if available)
        int retryAfter = 60; // Default to 60 seconds
        if (ex.Data.Contains("Retry-After") && int.TryParse(ex.Data["Retry-After"]?.ToString(), out var seconds))
        {
            retryAfter = seconds+1;
        }
        Console.WriteLine($"Rate limited. Waiting {retryAfter} seconds before retrying...");
        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
    }
}
