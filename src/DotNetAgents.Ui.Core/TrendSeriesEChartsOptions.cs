using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAgents.Ui.Core;

/// <summary>
/// Maps <see cref="TrendSeriesModel"/> (dashboard widget contract) to Apache ECharts option JSON for a simple line series.
/// Use with <c>EChartsPanel</c>; keep C# contracts as the source of truth and map in a thin adapter layer.
/// </summary>
/// <remarks>
/// Product policy: prefer **ECharts** (or Observable Plot / Vega-Lite per scenario) for new dashboards;
/// legacy charting references should stay outside the public package.
/// </remarks>
public static class TrendSeriesEChartsOptions
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>Builds a minimal line chart option object for <see cref="TrendSeriesModel.Points"/>.</summary>
    public static string ToLineChartOptionJson(
        TrendSeriesModel series,
        TrendSeriesEChartsLineFormatting? formatting = null)
    {
        formatting ??= TrendSeriesEChartsLineFormatting.Default;

        if (series.Points.Count == 0)
        {
            var empty = new JsonObject
            {
                ["title"] = new JsonObject
                {
                    ["text"] = formatting.EmptyTitle,
                    ["left"] = "center",
                    ["top"] = "middle"
                },
                ["xAxis"] = new JsonObject { ["type"] = "category", ["data"] = new JsonArray() },
                ["yAxis"] = new JsonObject { ["type"] = "value" },
                ["series"] = new JsonArray()
            };
            return empty.ToJsonString(SerializerOptions);
        }

        var categories = new JsonArray();
        foreach (var point in series.Points)
        {
            categories.Add(point.Timestamp.ToString(formatting.TimestampLabelFormat, formatting.FormatProvider));
        }

        var data = new JsonArray();
        foreach (var point in series.Points)
        {
            data.Add(JsonValue.Create(point.Value));
        }

        var seriesNode = new JsonObject
        {
            ["name"] = series.Label,
            ["type"] = "line",
            ["smooth"] = formatting.Smooth,
            ["showSymbol"] = formatting.ShowSymbol,
            ["data"] = data
        };

        var lineColor = formatting.ResolveLineColor(series.Intent);
        if (lineColor is not null)
        {
            seriesNode["lineStyle"] = new JsonObject { ["color"] = lineColor };
        }

        var root = new JsonObject
        {
            ["tooltip"] = new JsonObject { ["trigger"] = "axis" },
            ["grid"] = new JsonObject
            {
                ["left"] = "3%",
                ["right"] = "4%",
                ["bottom"] = "3%",
                ["containLabel"] = true
            },
            ["xAxis"] = new JsonObject
            {
                ["type"] = "category",
                ["boundaryGap"] = false,
                ["data"] = categories
            },
            ["yAxis"] = new JsonObject { ["type"] = "value" },
            ["series"] = new JsonArray { seriesNode }
        };

        return root.ToJsonString(SerializerOptions);
    }
}

/// <summary>Formatting for <see cref="TrendSeriesEChartsOptions.ToLineChartOptionJson"/>.</summary>
public sealed record TrendSeriesEChartsLineFormatting(
    string TimestampLabelFormat = "HH:mm",
    bool Smooth = true,
    bool ShowSymbol = false,
    string EmptyTitle = "No data",
    IFormatProvider? FormatProvider = null,
    Func<UiIntent, string?>? LineColorForIntent = null)
{
    internal static TrendSeriesEChartsLineFormatting Default { get; } = new();

    internal string? ResolveLineColor(UiIntent intent)
    {
        if (LineColorForIntent is not null)
        {
            return LineColorForIntent(intent);
        }

        return intent switch
        {
            UiIntent.Primary => "#1b6ec2",
            UiIntent.Info => "#0aa2c0",
            UiIntent.Success => "#198754",
            UiIntent.Warning => "#d39e00",
            UiIntent.Danger => "#dc3545",
            UiIntent.Neutral => "#6c757d",
            _ => "#1b6ec2"
        };
    }
}
