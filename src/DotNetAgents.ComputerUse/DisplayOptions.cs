namespace DotNetAgents.ComputerUse;

/// <summary>Options for screen capture (full screen, region, monitor). FR-CU-001.</summary>
public sealed record DisplayOptions
{
    /// <summary>Capture full primary screen when true; otherwise use Region.</summary>
    public bool FullScreen { get; init; } = true;

    /// <summary>Region to capture when FullScreen is false (X, Y, Width, Height).</summary>
    public ScreenRegion? Region { get; init; }

    /// <summary>Monitor index for multi-monitor (0 = primary).</summary>
    public int MonitorIndex { get; init; }
}

/// <summary>Rectangle for screen region capture.</summary>
public sealed record ScreenRegion(int X, int Y, int Width, int Height);
