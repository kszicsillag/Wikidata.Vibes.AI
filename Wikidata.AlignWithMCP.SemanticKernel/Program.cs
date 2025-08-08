using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

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

// Set up the OpenAI connector for Semantic Kernel to use GitHub Models API
var kernel = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(
        modelId: "gpt-5", // Replace with the actual model name, e.g., "gpt-5" if available
        apiKey: githubModelsApiKey,
        endpoint: new Uri("https://models.github.ai/inference")
    )
    .Build();

// Example: Send a prompt to the model
var prompt = "Write a C# function that reverses a string.";
var result = await kernel.InvokePromptAsync(prompt);
Console.WriteLine($"Model response: {result}");
