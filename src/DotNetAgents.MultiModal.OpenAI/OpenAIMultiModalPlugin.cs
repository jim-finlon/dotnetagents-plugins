using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.MultiModal.OpenAI;

/// <summary>
/// Plugin metadata for the OpenAI multi-modal package.
/// </summary>
public sealed class OpenAIMultiModalPlugin : PluginBase, IPluginWithCapabilityMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIMultiModalPlugin"/> class.
    /// </summary>
    public OpenAIMultiModalPlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "plugin-openai-multimodal",
            Name = "OpenAI MultiModal Plugin",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides OpenAI vision, transcription, and speech synthesis integration for multi-modal agents.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "MultiModal",
            Tags = new List<string> { "openai", "multimodal", "vision", "audio", "tts", "plugin" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/providers.md"
        };
    }

    /// <inheritdoc />
    public PluginCapabilityMetadata CapabilityMetadata { get; } = new(
        ProviderId: "openai-multimodal",
        SupportedModalities: new[] { "text", "vision", "transcription", "speech-synthesis" },
        SupportsStreaming: true,
        SupportsToolCalling: false,
        DeploymentKind: PluginDeploymentKind.Cloud,
        CredentialExpectations: new[]
        {
            new PluginCredentialExpectation(
                "providers/openai",
                "api_key",
                Description: "OpenAI API key reference held by CredentialsAgent.")
        },
        DefaultModelConfigurationKey: nameof(OpenAIMultiModalOptions.ModelName));

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation("OpenAI MultiModal plugin initialized. Use AddDotNetAgentsOpenAIMultiModal() to configure.");
        return Task.CompletedTask;
    }
}
