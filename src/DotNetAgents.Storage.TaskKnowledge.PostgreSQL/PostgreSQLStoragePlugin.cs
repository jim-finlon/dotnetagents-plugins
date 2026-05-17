using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// Plugin for PostgreSQL storage implementations.
/// </summary>
public class PostgreSQLStoragePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLStoragePlugin"/> class.
    /// </summary>
    public PostgreSQLStoragePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "storage-postgresql",
            Name = "PostgreSQL Storage",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides PostgreSQL implementations for checkpoint stores, task stores, and knowledge stores. Supports workflow checkpointing, task persistence, and knowledge management.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "storage", "postgresql", "database", "checkpoint", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/storage.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "PostgreSQL Storage plugin initialized. Use AddPostgreSQLCheckpointStore(), AddPostgreSQLTaskStore(), or AddPostgreSQLKnowledgeStore() to configure.");

        return Task.CompletedTask;
    }
}
