using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Qdrant;

/// <summary>
/// Plugin for Qdrant vector store implementation.
/// </summary>
public class QdrantVectorStorePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorStorePlugin"/> class.
    /// </summary>
    public QdrantVectorStorePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "vectorstore-qdrant",
            Name = "Qdrant Vector Store",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Qdrant high-performance vector database implementation. Supports both self-hosted and cloud deployments with advanced filtering capabilities.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "vector-store", "qdrant", "embeddings", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/vector-stores.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Qdrant Vector Store plugin initialized. Use AddQdrantVectorStore() to configure.");

        return Task.CompletedTask;
    }
}
