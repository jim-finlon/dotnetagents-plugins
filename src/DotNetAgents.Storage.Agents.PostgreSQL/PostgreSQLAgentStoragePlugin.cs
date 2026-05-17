using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Storage.Agents.PostgreSQL;

/// <summary>
/// Plugin for PostgreSQL agent storage implementations.
/// </summary>
public class PostgreSQLAgentStoragePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLAgentStoragePlugin"/> class.
    /// </summary>
    public PostgreSQLAgentStoragePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "storage-agents-postgresql",
            Name = "PostgreSQL Agent Storage",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides PostgreSQL implementations for agent registry and task queue storage. Enables distributed agent management and task persistence.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "storage", "postgresql", "agents", "registry", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/storage.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "PostgreSQL Agent Storage plugin initialized. Use AddPostgreSQLAgentRegistry() or AddPostgreSQLTaskQueue() to configure.");

        return Task.CompletedTask;
    }
}
