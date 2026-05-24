namespace DotNetAgents.MediaGeneration.ComfyUI;

/// <summary>Options for ComfyUI API connection.</summary>
public sealed class ComfyUIOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8188";
    public string? ClientId { get; set; }
    /// <summary>Directory to save generated video/image files. If null, a temp path is used.</summary>
    public string? OutputDirectory { get; set; }
    public TimeSpan QueueTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
