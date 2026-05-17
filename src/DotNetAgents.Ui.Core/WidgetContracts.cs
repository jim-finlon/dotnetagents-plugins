namespace DotNetAgents.Ui.Core;

// Dashboard widget contracts. For agent-authored decks (text, media, charts, diagrams), see PresentationComposition.cs.

public enum TrendDirection
{
    Flat = 0,
    Up = 1,
    Down = 2
}

public enum StreamConnectionState
{
    Connected = 0,
    Connecting = 1,
    Reconnecting = 2,
    Disconnected = 3
}

public sealed record KpiWidgetModel(
    string Id,
    string Label,
    string Value,
    string? SubLabel = null,
    double? DeltaPercent = null,
    TrendDirection Trend = TrendDirection.Flat,
    UiIntent Intent = UiIntent.Neutral);

public sealed record TrendPoint(
    DateTimeOffset Timestamp,
    double Value);

public sealed record TrendSeriesModel(
    string Id,
    string Label,
    IReadOnlyList<TrendPoint> Points,
    UiIntent Intent = UiIntent.Primary);

public sealed record FilterOption(
    string Value,
    string Label);

public sealed record FilterDescriptor(
    string Id,
    string Label,
    string? SelectedValue,
    IReadOnlyList<FilterOption> Options);

public sealed record GridColumnDefinition(
    string Key,
    string Header,
    bool Numeric = false,
    string? Width = null);

public sealed record GridRowModel(
    string Id,
    IReadOnlyDictionary<string, string> Cells,
    UiIntent Intent = UiIntent.Neutral);

public sealed record StreamWidgetState(
    StreamConnectionState ConnectionState,
    DateTimeOffset? LastUpdatedUtc = null,
    bool IsStale = false,
    string? Message = null);

public sealed record StreamEventModel(
    string Id,
    DateTimeOffset Timestamp,
    string Title,
    string Source,
    string Summary,
    UiIntent Intent = UiIntent.Neutral);

/// <summary>Card on a workflow board. Optional <c>PreviewImageUrl</c> for evidence thumbnails; optional <c>Href</c> for a detail page (e.g. <c>/story/{id}</c>).</summary>
public sealed record BoardCardModel(
    string Id,
    string Title,
    string? Badge = null,
    string? Meta = null,
    string? SecondaryText = null,
    IReadOnlyList<string>? Highlights = null,
    UiIntent Intent = UiIntent.Neutral,
    string? PreviewImageUrl = null,
    string? Href = null);

public sealed record BoardColumnModel(
    string Id,
    string Label,
    IReadOnlyList<BoardCardModel> Cards,
    string? Summary = null,
    string? EmptyState = null,
    UiIntent Intent = UiIntent.Neutral);

/// <summary>Single tile in a <c>SummaryMetricGrid</c> (readiness/review KPI strip).</summary>
public sealed record SummaryMetricModel(
    string Id,
    string Label,
    string Value,
    string? Note = null,
    UiIntent Intent = UiIntent.Neutral);

public sealed record NavItemModel(
    string Id,
    string Label,
    string Href,
    string Icon = "grid",
    string? Badge = null,
    IReadOnlyList<NavItemModel>? Children = null,
    string? NavGroup = null);

public sealed record BreadcrumbItem(
    string Label,
    string? Href = null);
