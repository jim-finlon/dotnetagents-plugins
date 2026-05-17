using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Pinecone;

/// <summary>
/// Plugin for Pinecone vector store implementation.
/// </summary>
public class PineconeVectorStorePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PineconeVectorStorePlugin"/> class.
    /// </summary>
    public PineconeVectorStorePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "vectorstore-pinecone",
            Name = "Pinecone Vector Store",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Pinecone cloud vector database implementation for high-performance vector similarity search. Supports managed cloud infrastructure with automatic scaling.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "vector-store", "pinecone", "cloud", "embeddings", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/vector-stores.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Pinecone Vector Store plugin initialized. Use AddPineconeVectorStore() to configure.");

        return Task.CompletedTask;
    }
}
