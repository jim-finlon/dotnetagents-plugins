using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.PostgreSQL;

/// <summary>
/// Plugin for PostgreSQL vector store implementation.
/// </summary>
public class PostgreSQLVectorStorePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLVectorStorePlugin"/> class.
    /// </summary>
    public PostgreSQLVectorStorePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "vectorstore-postgresql",
            Name = "PostgreSQL Vector Store",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides PostgreSQL (pgvector) implementation for vector similarity search. Supports cosine, Euclidean, and inner product distance metrics.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "vector-store", "postgresql", "pgvector", "embeddings", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/vector-stores.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "PostgreSQL Vector Store plugin initialized. Use AddPostgreSQLVectorStore() to configure.");

        return Task.CompletedTask;
    }
}
