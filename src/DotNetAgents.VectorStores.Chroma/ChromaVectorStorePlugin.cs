using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Chroma;

/// <summary>
/// Plugin for Chroma vector store implementation.
/// </summary>
public class ChromaVectorStorePlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaVectorStorePlugin"/> class.
    /// </summary>
    public ChromaVectorStorePlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "vectorstore-chroma",
            Name = "Chroma Vector Store",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Chroma embedding database implementation. Supports both local and server deployments with simple API.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "vector-store", "chroma", "embeddings", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/vector-stores.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Chroma Vector Store plugin initialized. Use AddChromaVectorStore() to configure.");

        return Task.CompletedTask;
    }
}
