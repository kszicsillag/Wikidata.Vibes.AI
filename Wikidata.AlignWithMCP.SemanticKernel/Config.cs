using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Wikidata.AlignWithMCP.SemanticKernel;

public class WikidataMcpOptions
{
    [Required]
    public string Path { get; set; } = string.Empty;
    [Required]
    public string WorkingDirectory { get; set; } = string.Empty;
}

public class PostgreSQLMcpOptions
{
    [Required]
    public string Path { get; set; } = string.Empty;
    [Required]
    public string WorkingDirectory { get; set; } = string.Empty;
    [Required]
    public string DatabaseUri { get; set; } = string.Empty;
}

public class GitHubModelsOptions
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;
}

