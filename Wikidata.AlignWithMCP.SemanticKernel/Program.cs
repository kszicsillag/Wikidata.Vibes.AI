using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelContextProtocol.Client;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Wikidata.AlignWithMCP.SemanticKernel;
using FunctionCallContent = Microsoft.SemanticKernel.FunctionCallContent;
using FunctionResultContent = Microsoft.SemanticKernel.FunctionResultContent;

// Build configuration and validate settings
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddOptions<GitHubModelsOptions>().Bind(configuration.GetSection("GitHubModels")).ValidateDataAnnotations();
services.AddOptions<WikidataMcpOptions>().Bind(configuration.GetSection("McpServers:WikidataMcp")).ValidateDataAnnotations();
services.AddOptions<PostgreSQLMcpOptions>().Bind(configuration.GetSection("McpServers:PostgreSQLMcp")).ValidateDataAnnotations();

var provider = services.BuildServiceProvider();
var githubOptions = provider.GetRequiredService<IOptions<GitHubModelsOptions>>().Value;
var wikidataOptions = provider.GetRequiredService<IOptions<WikidataMcpOptions>>().Value;
var postgresqlOptions = provider.GetRequiredService<IOptions<PostgreSQLMcpOptions>>().Value;

// Configure OpenTelemetry
//var (traceProvider, meterProvider, loggerFactory) = TelemetrySetup.ConfigureOpenTelemetry();

const string WikidataMcpName = "WikidataMCP";
const string PostgreSqlMcpName = "PostgreSQLMCP";

await using IMcpClient wikidataMcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = WikidataMcpName,
        WorkingDirectory = wikidataOptions.WorkingDirectory,
        Command = "uv",
        Arguments = ["run", wikidataOptions.Path],
    }));

await using IMcpClient postgreSqlMcpClient = await McpClientFactory.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = PostgreSqlMcpName,
        WorkingDirectory = postgresqlOptions.WorkingDirectory,
        Command = "uv",
        Arguments = ["run", postgresqlOptions.Path, "--access-mode=restricted"],
        EnvironmentVariables = new Dictionary<string,string?> {{"DATABASE_URI", postgresqlOptions.DatabaseUri}}
    }));

var wikidataTools = await wikidataMcpClient.ListToolsAsync();
var postgreSqlTools = await postgreSqlMcpClient.ListToolsAsync();

// Set up the OpenAI connector for Semantic Kernel to use GitHub Models API
IKernelBuilder kbuilder = Kernel.CreateBuilder();
//kbuilder.Services.AddSingleton(loggerFactory);
var kernel = kbuilder.AddOpenAIChatCompletion(
        modelId: "gpt-4.1-mini",
        apiKey: githubOptions.ApiKey,
        endpoint: new Uri("https://models.github.ai/inference")
    )
    .Build();

#pragma warning disable SKEXP0001
kernel.Plugins.AddFromFunctions(WikidataMcpName, wikidataTools.Select(aiFunction => aiFunction.AsKernelFunction()));
kernel.Plugins.AddFromFunctions(PostgreSqlMcpName, postgreSqlTools.Select(aiFunction => aiFunction.AsKernelFunction()));
// Load agent instructions from .md file
var instructionsPath = Path.Combine(AppContext.BaseDirectory, "agent-instructions.md");
string agentInstructions = File.ReadAllText(instructionsPath);

// Create ChatCompletionAgent with ReAct style instructions
var agent = new ChatCompletionAgent
{
    Instructions = agentInstructions,
    Name = "WikidataMusicBrainzReActAgent",
    Kernel = kernel,
    Arguments = new KernelArguments(new OpenAIPromptExecutionSettings()
    {
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
    })
};
#pragma warning restore SKEXP0001

AgentInvokeOptions options = new AgentInvokeOptions
{
    OnIntermediateMessage = HandleIntermediateMessage
};

ConsoleHelpers.PrintGreetings("Wikidata Aligner v0.1");

// Example: Send a prompt to the model
//var prompt = "What is the capital of Hungary according to Wikidata?";
//var prompt = "When is Nightwish founded according to MusicBrainZ?";
var prompt = "Who where the founding members of Nightwish according to MusicBrainZ?";

var maxRetries = 5;
for (int attempt = 0; attempt < maxRetries; attempt++)
{
    try
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(prompt);
        await foreach (var responseItem in agent.InvokeAsync(chatHistory, options: options))
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

return;

Task HandleIntermediateMessage(ChatMessageContent message)
{
    foreach (var item in message.Items)
    {
        if (item is FunctionCallContent call)
        {
            ConsoleHelpers.PrintArgumentsTable(call.Arguments, call.FunctionName);
        }
        else if (item is FunctionResultContent result)
        {
            if (result.Result != null)
            {
                ConsoleHelpers.PrintArgumentsTable(result.Result.AsDictionary(), $"{result.PluginName}.{result.FunctionName}");
            }
            else
            {
                ConsoleHelpers.PrintSimpleMessage($"{result.PluginName}.{result.FunctionName} : no result!");
            }
        }
        else
            ConsoleHelpers.PrintSimpleMessage($"{item.GetType().FullName} {message.Role}: {message.Content}");
    }
    return Task.CompletedTask;
}
