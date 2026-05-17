namespace DotNetAgents.MultiModal.OpenAI;

/// <summary>
/// Configuration options for OpenAI multi-modal services.
/// </summary>
public sealed class OpenAIMultiModalOptions
{
    /// <summary>
    /// Gets or sets the OpenAI API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default text/audio model name.
    /// </summary>
    public string ModelName { get; set; } = "gpt-4o";

    /// <summary>
    /// Gets or sets the optional vision model name.
    /// </summary>
    public string? VisionModel { get; set; }

    /// <summary>
    /// Gets or sets an optional action to configure the HTTP client.
    /// </summary>
    public Action<HttpClient>? ConfigureHttpClient { get; set; }

    /// <summary>
    /// Validates options required to construct the OpenAI multi-modal model.
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(ModelName);
    }
}
