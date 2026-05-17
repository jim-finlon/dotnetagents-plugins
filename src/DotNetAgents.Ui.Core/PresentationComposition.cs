namespace DotNetAgents.Ui.Core;

/// <summary>
/// Versioned document for agent- or API-authored "decks": ordered blocks that hosts render with shared UI kit components.
/// Aligns with DNA UI composition contract (markdown, media, charts, diagrams).
/// </summary>
public sealed record PresentationDeckDocument(
    string SchemaVersion,
    IReadOnlyList<PresentationBlock> Blocks);

/// <summary>
/// Discriminated block kinds for teaching flows, dashboards, and export adapters (e.g. Slideshow Agent).
/// </summary>
public abstract record PresentationBlock;

public sealed record TextPresentationBlock(
    string Markdown,
    string? Title = null) : PresentationBlock;

public enum MediaKind
{
    Image = 0,
    Video = 1,
    Audio = 2
}

public sealed record MediaPresentationBlock(
    MediaKind Kind,
    string DisplayUrl,
    string? AltText = null,
    string? Title = null,
    int Order = 0) : PresentationBlock;

public enum ChartEngine
{
    ECharts = 0,
    ObservablePlot = 1,
    VegaLite = 2,
    D3 = 3
}

/// <summary>
/// Chart payload: engine id + option/spec JSON (ECharts option object, Vega-Lite spec, or D3 module payload).
/// </summary>
public sealed record ChartPresentationBlock(
    ChartEngine Engine,
    string OptionsJson,
    string? Title = null,
    int? HeightPx = null) : PresentationBlock;

public sealed record MermaidPresentationBlock(
    string Markup,
    string? Title = null) : PresentationBlock;

public sealed record D3PresentationBlock(
    string ModuleId,
    string DataJson,
    string? Title = null,
    int? HeightPx = null) : PresentationBlock;

/// <summary>
/// Opaque embed (iframe URL, trusted component name, etc.) — host-specific.
/// </summary>
public sealed record EmbedPresentationBlock(
    string EmbedKind,
    string PayloadJson) : PresentationBlock;
