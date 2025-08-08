using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Wikidata.AlignWithMCP.SemanticKernel;

// Build configuration and validate settings
var configuration = ConfigurationSetup.BuildConfiguration();
var appConfig = ConfigurationSetup.LoadAndValidateConfiguration(configuration);
// Configure OpenTelemetry
var (traceProvider, meterProvider, loggerFactory) = TelemetrySetup.ConfigureOpenTelemetry();

const string WikidataMcpName = "WikidataMCP";

await using IMcpClient mcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = WikidataMcpName,
        WorkingDirectory = appConfig.WikidataMcpWorkingDirectory,
        Command = "uv",
        Arguments = ["run", appConfig.WikidataMcpPath],
    }));

var tools = await mcpClient.ListToolsAsync();

// Set up the OpenAI connector for Semantic Kernel to use GitHub Models API
IKernelBuilder kbuilder = Kernel.CreateBuilder();
kbuilder.Services.AddSingleton(loggerFactory);
var kernel = kbuilder.AddOpenAIChatCompletion(
        modelId: "gpt-4.1-mini",
        apiKey: appConfig.GitHubModelsApiKey,
        endpoint: new Uri("https://models.github.ai/inference")
    )
    .Build();

#pragma warning disable SKEXP0001
kernel.Plugins.AddFromFunctions(WikidataMcpName, tools.Select(aiFunction => aiFunction.AsKernelFunction()));

// Load agent instructions from .md file
var instructionsPath = Path.Combine(AppContext.BaseDirectory, "agent-instructions.md");
string agentInstructions = File.ReadAllText(instructionsPath);

// Create ChatCompletionAgent with ReAct style instructions
var agent = new ChatCompletionAgent()
{
    Instructions = agentInstructions,
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
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(prompt);
        await foreach (var responseItem in agent.InvokeAsync(chatHistory))
        {
            Console.WriteLine($"Agent response: {responseItem.Message}");
        }
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
