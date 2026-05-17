using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Weaviate;

/// <summary>
/// Plugin for Weaviate vector store implementation.
/// </summary>
public class WeaviateVectorStorePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateVectorStorePlugin"/> class.
    /// </summary>
    public WeaviateVectorStorePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "vectorstore-weaviate",
            Name = "Weaviate Vector Store",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Weaviate open-source vector database implementation. Supports GraphQL queries, hybrid search, and semantic understanding.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "vector-store", "weaviate", "graphql", "embeddings", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/vector-stores.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Weaviate Vector Store plugin initialized. Use AddWeaviateVectorStore() to configure.");

        return Task.CompletedTask;
    }
}
