using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Blazor.Components.Widgets;

public sealed record ModelVersionRolloutDashboardModel(
    IReadOnlyList<ModelVersionRolloutRow> ActiveRollouts,
    IReadOnlyList<ModelVersionPromotionDecisionEvent> RecentDecisions,
    DateTimeOffset GeneratedAtUtc,
    string DataSourceRef);

public sealed record ModelVersionRolloutRow(
    string ModelVersionId,
    string DisplayName,
    string CurrentStage,
    TimeSpan DwellRemaining,
    int SamplesObserved,
    int MinSamples,
    double ScorecardDelta,
    double ErrorRateDelta,
    bool WatchdogEnabled,
    DateTimeOffset NextDecisionAtUtc,
    string Status);

public sealed record ModelVersionPromotionDecisionEvent(
    DateTimeOffset TimestampUtc,
    string ModelVersionId,
    string FromStage,
    string ToStage,
    string GateVerdict,
    string ScorecardRef,
    string TriggeredBy,
    string? Reason);

public enum ModelVersionRolloutCommandKind
{
    Hold,
    SkipToNextStage,
    Abort,
    DisableWatchdog,
    SetThresholds
}

public sealed record ModelVersionRolloutCommand(
    ModelVersionRolloutCommandKind Kind,
    string ModelVersionId,
    string? TargetStage = null,
    string? Reason = null);

internal static class ModelVersionRolloutIntent
{
    public static UiIntent FromDelta(double value, bool lowerIsBetter = false)
    {
        if (Math.Abs(value) < 0.001)
            return UiIntent.Neutral;

        var isGood = lowerIsBetter ? value < 0 : value > 0;
        return isGood ? UiIntent.Success : UiIntent.Warning;
    }

    public static UiIntent FromVerdict(string verdict) =>
        verdict.Equals("passed", StringComparison.OrdinalIgnoreCase) ||
        verdict.Equals("promoted", StringComparison.OrdinalIgnoreCase)
            ? UiIntent.Success
            : verdict.Equals("blocked", StringComparison.OrdinalIgnoreCase) ||
              verdict.Equals("rollback", StringComparison.OrdinalIgnoreCase)
                ? UiIntent.Danger
                : UiIntent.Warning;
}
