namespace DotNetAgents.ComputerUse;

/// <summary>Multi-step automation plan for high-level task execution. CU-3.6.</summary>
public sealed record AutomationPlan
{
    /// <summary>Human-readable goal.</summary>
    public string Goal { get; init; } = string.Empty;

    /// <summary>Ordered steps to execute.</summary>
    public IReadOnlyList<AutomationStep> Steps { get; init; } = Array.Empty<AutomationStep>();
}

/// <summary>Single step in an automation plan. CU-3.6.</summary>
public sealed record AutomationStep
{
    /// <summary>Step kind (e.g. Navigate, Click, Fill).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Parameters (e.g. "url", "selector", "value").</summary>
    public IReadOnlyDictionary<string, string?> Parameters { get; init; } = new Dictionary<string, string?>();
}
