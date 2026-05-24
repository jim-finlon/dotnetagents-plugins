namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Request for image generation (e.g. SDXL storyboard).</summary>
public sealed class ImageGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public string? InputImagePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Seed { get; init; }
    public string? PreferredWorkerId { get; init; }

    /// <summary>ComfyUI workflow template name (file <c>{name}.json</c> under the loader path). Default <c>sdxl-default</c>.</summary>
    public string WorkflowTemplate { get; init; } = "sdxl-default";

    /// <summary>Optional extra placeholders for the workflow (e.g. <c>ckpt_name</c>).</summary>
    public IReadOnlyDictionary<string, string>? TemplateExtraVariables { get; init; }
}
