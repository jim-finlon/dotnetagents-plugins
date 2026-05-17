namespace DotNetAgents.ComputerUse;

/// <summary>Detected UI element (bounding box, description). FR-CU-002.</summary>
public sealed record ElementInfo
{
    /// <summary>Stable identifier for the element.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Description or label (e.g. from vision/OCR).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Bounding box (screen coordinates).</summary>
    public ScreenRegion Bounds { get; init; } = new(0, 0, 0, 0);

    /// <summary>Center point for click.</summary>
    public ScreenCoordinates Center => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);
}
