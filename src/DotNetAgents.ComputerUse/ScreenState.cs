namespace DotNetAgents.ComputerUse;

/// <summary>Captured screen content and metadata. FR-CU-001.</summary>
public sealed record ScreenState
{
    /// <summary>Image bytes (e.g. PNG).</summary>
    public byte[] ImageBytes { get; init; } = Array.Empty<byte>();

    /// <summary>MIME type of image (e.g. "image/png").</summary>
    public string ContentType { get; init; } = "image/png";

    /// <summary>Width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; init; }
}
