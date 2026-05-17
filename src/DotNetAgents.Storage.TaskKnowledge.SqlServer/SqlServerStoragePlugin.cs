using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Storage.SqlServer;

/// <summary>
/// Plugin for SQL Server storage implementations.
/// </summary>
public class SqlServerStoragePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerStoragePlugin"/> class.
    /// </summary>
    public SqlServerStoragePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "storage-sqlserver",
            Name = "SQL Server Storage",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides SQL Server implementations for checkpoint stores, task stores, and knowledge stores. Supports workflow checkpointing, task persistence, and knowledge management.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "storage", "sqlserver", "database", "checkpoint", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/storage.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "SQL Server Storage plugin initialized. Use AddSqlServerCheckpointStore(), AddSqlServerTaskStore(), or AddSqlServerKnowledgeStore() to configure.");

        return Task.CompletedTask;
    }
}
