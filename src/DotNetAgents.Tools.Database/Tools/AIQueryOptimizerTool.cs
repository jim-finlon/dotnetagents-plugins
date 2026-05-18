using DotNetAgents.Abstractions.Tools;
using DotNetAgents.Database.AI;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for optimizing database queries using AI.
/// </summary>
public class AIQueryOptimizerTool : ITool
{
    private readonly AIQueryOptimizer _optimizer;
    private static readonly JsonElement _inputSchema;

    static AIQueryOptimizerTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": {
                    ""type"": ""string"",
                    ""description"": ""The SQL query to optimize""
                },
                ""schema_context"": {
                    ""type"": ""string"",
                    ""description"": ""Optional schema context information""
                }
            },
            ""required"": [""query""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIQueryOptimizerTool"/> class.
    /// </summary>
    /// <param name="optimizer">The AI query optimizer.</param>
    public AIQueryOptimizerTool(AIQueryOptimizer optimizer)
    {
        _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
    }

    /// <inheritdoc/>
    public string Name => "ai_query_optimizer";

    /// <inheritdoc/>
    public string Description => "Optimizes database queries using AI, providing PostgreSQL-specific optimizations, index recommendations, and performance improvements.";

    /// <inheritdoc/>
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("query", out var queryObj) || queryObj is not string query)
        {
            return ToolResult.Failure("Missing or invalid 'query' parameter.");
        }

        var schemaContext = parameters.TryGetValue("schema_context", out var schemaObj) && schemaObj is string schema
            ? schema
            : null;

        try
        {
            var result = await _optimizer.OptimizeAsync(query, schemaContext, cancellationToken).ConfigureAwait(false);

            var output = new Dictionary<string, object>
            {
                ["original_query"] = result.OriginalQuery,
                ["optimized_query"] = result.OptimizedQuery ?? result.OriginalQuery,
                ["suggestions"] = result.Suggestions.Select(s => new Dictionary<string, object>
                {
                    ["type"] = s.Type,
                    ["description"] = s.Description,
                    ["impact"] = s.Impact
                }).ToList(),
                ["estimated_improvement_percent"] = result.EstimatedImprovementPercent ?? 0,
                ["confidence_score"] = result.ConfidenceScore,
                ["warnings"] = result.Warnings
            };

            return ToolResult.Success(
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                output);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Query optimization failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["query"] = query
                });
        }
    }

    private static IDictionary<string, object> ParseInput(object input)
    {
        if (input is JsonElement jsonElement)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in jsonElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()!,
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => prop.Value,
                    JsonValueKind.Array => prop.Value,
                    _ => prop.Value.ToString()
                };
            }
            return dict;
        }

        if (input is IDictionary<string, object> dictInput)
        {
            return dictInput;
        }

        throw new ArgumentException("Input must be JsonElement or IDictionary<string, object>", nameof(input));
    }
}
