using DotNetAgents.Abstractions.Models;
using DotNetAgents.Database.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAgents.Database.AI;

/// <summary>
/// AI-powered intelligent type mapper that analyzes data patterns to suggest optimal database types.
/// </summary>
public sealed class AITypeMapper
{
    private readonly ILLMModel<string, string> _llm;
    private readonly ILogger<AITypeMapper>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AITypeMapper"/> class.
    /// </summary>
    /// <param name="llm">The LLM model to use for type mapping.</param>
    /// <param name="logger">Optional logger instance.</param>
    public AITypeMapper(
        ILLMModel<string, string> llm,
        ILogger<AITypeMapper>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    /// <summary>
    /// Suggests optimal type mapping for a column based on data analysis.
    /// </summary>
    /// <param name="column">The column to analyze.</param>
    /// <param name="dataDistribution">Optional data distribution information.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The type mapping recommendation.</returns>
    public async Task<TypeMappingRecommendation> SuggestMappingAsync(
        Column column,
        string? dataDistribution = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(column);

        _logger?.LogInformation("Analyzing type mapping for column: {ColumnName}", column.Name);

        try
        {
            var prompt = BuildTypeMappingPrompt(column, dataDistribution);

            var options = new LLMOptions
            {
                Temperature = 0.2, // Lower temperature for consistent type recommendations
                MaxTokens = 2000
            };

            var response = await _llm.GenerateAsync(prompt, options, cancellationToken).ConfigureAwait(false);

            return ParseMappingResponse(response, column);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during type mapping analysis for column {ColumnName}", column.Name);
            throw;
        }
    }

    private static string BuildTypeMappingPrompt(Column column, string? dataDistribution)
    {
        var prompt = $@"You are an expert database migration specialist. Analyze column data patterns and suggest optimal PostgreSQL type mappings based on storage efficiency, performance, and data characteristics.

Column Information:
- Name: {column.Name}
- Current Type: {column.DataType}
- Max Length: {column.MaxLength?.ToString() ?? "N/A"}
- Precision: {column.Precision?.ToString() ?? "N/A"}
- Scale: {column.Scale?.ToString() ?? "N/A"}
- Is Nullable: {column.IsNullable}
- Is Identity: {column.IsIdentity}

{(string.IsNullOrWhiteSpace(dataDistribution) ? "" : $"Data Distribution:\n{dataDistribution}\n")}

Provide a type mapping recommendation as JSON:
{{
  ""recommended_type"": ""postgresql_type"",
  ""rationale"": ""explanation"",
  ""alternatives"": [
    {{
      ""type"": ""alternative_type"",
      ""description"": ""description"",
      ""use_when"": ""when to use""
    }}
  ],
  ""confidence_score"": 85,
  ""storage_comparison"": ""storage comparison"",
  ""performance_implications"": ""performance notes""
}}";

        return prompt;
    }

    private TypeMappingRecommendation ParseMappingResponse(string response, Column column)
    {
        try
        {
            // Try to parse as JSON first
            var jsonMatch = Regex.Match(response, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Groups[1].Value;
                var parsed = JsonSerializer.Deserialize<TypeMappingRecommendation>(json);
                if (parsed != null)
                {
                    return parsed with
                    {
                        ColumnName = column.Name,
                        OriginalType = column.DataType
                    };
                }
            }

            // Try direct JSON parse
            var directParse = JsonSerializer.Deserialize<TypeMappingRecommendation>(response);
            if (directParse != null && (directParse.ColumnName != column.Name || directParse.OriginalType != column.DataType))
            {
                return directParse with
                {
                    ColumnName = column.Name,
                    OriginalType = column.DataType
                };
            }
            if (directParse != null)
            {
                return directParse;
            }

            // Fallback: create basic recommendation
            return new TypeMappingRecommendation
            {
                ColumnName = column.Name,
                OriginalType = column.DataType,
                RecommendedType = column.DataType,
                Rationale = "See AI analysis",
                Alternatives = new List<AlternativeType>(),
                ConfidenceScore = 50
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse mapping response, using fallback");
            return new TypeMappingRecommendation
            {
                ColumnName = column.Name,
                OriginalType = column.DataType,
                RecommendedType = column.DataType,
                Rationale = "See AI analysis",
                Alternatives = new List<AlternativeType>(),
                ConfidenceScore = 50
            };
        }
    }
}
