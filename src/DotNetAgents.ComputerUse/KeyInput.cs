namespace DotNetAgents.ComputerUse;

/// <summary>Key or key combination for input injection. FR-CU-001.</summary>
public sealed record KeyInput
{
    /// <summary>Key name (e.g. "Enter", "Tab", "a").</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Modifiers (e.g. "Control", "Alt").</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = Array.Empty<string>();
}
