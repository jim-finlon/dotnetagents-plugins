using DotNetAgents.Abstractions.Tools;
using DotNetAgents.Database.AI;
using DotNetAgents.Database.Abstractions;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for intelligent database type mapping using AI.
/// </summary>
public class AITypeMapperTool : ITool
{
    private readonly AITypeMapper _typeMapper;
    private static readonly JsonElement _inputSchema;

    static AITypeMapperTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""column_name"": {
                    ""type"": ""string"",
                    ""description"": ""Column name""
                },
                ""data_type"": {
                    ""type"": ""string"",
                    ""description"": ""Current data type""
                },
                ""max_length"": {
                    ""type"": ""integer"",
                    ""description"": ""Maximum length (optional)""
                },
                ""precision"": {
                    ""type"": ""integer"",
                    ""description"": ""Precision (optional)""
                },
                ""scale"": {
                    ""type"": ""integer"",
                    ""description"": ""Scale (optional)""
                },
                ""is_nullable"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether column is nullable""
                },
                ""data_distribution"": {
                    ""type"": ""string"",
                    ""description"": ""Optional data distribution information""
                }
            },
            ""required"": [""column_name"", ""data_type""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AITypeMapperTool"/> class.
    /// </summary>
    /// <param name="typeMapper">The AI type mapper.</param>
    public AITypeMapperTool(AITypeMapper typeMapper)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
    }

    /// <inheritdoc/>
    public string Name => "ai_type_mapper";

    /// <inheritdoc/>
    public string Description => "Suggests optimal database type mappings using AI analysis of data patterns, storage efficiency, and performance characteristics.";

    /// <inheritdoc/>
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("column_name", out var nameObj) || nameObj is not string columnName)
        {
            return ToolResult.Failure("Missing or invalid 'column_name' parameter.");
        }

        if (!parameters.TryGetValue("data_type", out var typeObj) || typeObj is not string dataType)
        {
            return ToolResult.Failure("Missing or invalid 'data_type' parameter.");
        }

        var column = new Column
        {
            Name = columnName,
            DataType = dataType,
            IsNullable = parameters.TryGetValue("is_nullable", out var nullableObj) && nullableObj is bool nullable ? nullable : true,
            MaxLength = parameters.TryGetValue("max_length", out var maxLenObj) && maxLenObj is int maxLen ? maxLen : null,
            Precision = parameters.TryGetValue("precision", out var precObj) && precObj is int prec ? prec : null,
            Scale = parameters.TryGetValue("scale", out var scaleObj) && scaleObj is int scale ? scale : null
        };

        var dataDistribution = parameters.TryGetValue("data_distribution", out var distObj) && distObj is string dist
            ? dist
            : null;

        try
        {
            var result = await _typeMapper.SuggestMappingAsync(column, dataDistribution, cancellationToken).ConfigureAwait(false);

            var output = new Dictionary<string, object>
            {
                ["column_name"] = result.ColumnName,
                ["original_type"] = result.OriginalType,
                ["recommended_type"] = result.RecommendedType,
                ["rationale"] = result.Rationale,
                ["alternatives"] = result.Alternatives.Select(a => new Dictionary<string, object>
                {
                    ["type"] = a.Type,
                    ["description"] = a.Description ?? "",
                    ["use_when"] = a.UseWhen ?? ""
                }).ToList(),
                ["confidence_score"] = result.ConfidenceScore,
                ["storage_comparison"] = result.StorageComparison ?? "",
                ["performance_implications"] = result.PerformanceImplications ?? ""
            };

            return ToolResult.Success(
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                output);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Type mapping failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["column_name"] = columnName
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
