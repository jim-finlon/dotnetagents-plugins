using DotNetAgents.Abstractions.Models;
using DotNetAgents.Database.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAgents.Database.AI;

/// <summary>
/// AI-powered query optimizer for database queries.
/// </summary>
public sealed class AIQueryOptimizer
{
    private readonly ILLMModel<string, string> _llm;
    private readonly ILogger<AIQueryOptimizer>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIQueryOptimizer"/> class.
    /// </summary>
    /// <param name="llm">The LLM model to use for optimization.</param>
    /// <param name="logger">Optional logger instance.</param>
    public AIQueryOptimizer(
        ILLMModel<string, string> llm,
        ILogger<AIQueryOptimizer>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    /// <summary>
    /// Optimizes a database query using AI.
    /// </summary>
    /// <param name="query">The SQL query to optimize.</param>
    /// <param name="schemaContext">Optional schema context information.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The optimization result.</returns>
    public async Task<QueryOptimizationResult> OptimizeAsync(
        string query,
        string? schemaContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        _logger?.LogInformation("Optimizing query using AI");

        try
        {
            var prompt = BuildOptimizationPrompt(query, schemaContext);

            var options = new LLMOptions
            {
                Temperature = 0.3, // Lower temperature for more consistent optimizations
                MaxTokens = 3000
            };

            var response = await _llm.GenerateAsync(prompt, options, cancellationToken).ConfigureAwait(false);

            return ParseOptimizationResponse(response, query);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during query optimization");
            throw;
        }
    }

    private static string BuildOptimizationPrompt(string query, string? schemaContext)
    {
        var prompt = $@"You are an expert database query optimizer. Analyze the following SQL query and provide PostgreSQL-specific optimizations.

Query:
{query}

{(string.IsNullOrWhiteSpace(schemaContext) ? "" : $"Schema Context:\n{schemaContext}\n")}

Provide optimizations including:
- Index recommendations
- JOIN optimizations
- Window function opportunities
- Materialized view candidates
- Query structure improvements

Return your response as JSON with the following structure:
{{
  ""optimized_query"": ""optimized SQL query"",
  ""suggestions"": [
    {{
      ""type"": ""Index"",
      ""description"": ""description"",
      ""impact"": ""High|Medium|Low""
    }}
  ],
  ""estimated_improvement_percent"": 25,
  ""confidence_score"": 85,
  ""warnings"": []
}}";

        return prompt;
    }

    private QueryOptimizationResult ParseOptimizationResponse(string response, string originalQuery)
    {
        try
        {
            // Try to parse as JSON first
            var jsonMatch = Regex.Match(response, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Groups[1].Value;
                var parsed = JsonSerializer.Deserialize<QueryOptimizationResult>(json);
                if (parsed != null)
                {
                    return parsed with { OriginalQuery = originalQuery };
                }
            }

            // Try direct JSON parse
            var directParse = JsonSerializer.Deserialize<QueryOptimizationResult>(response);
            if (directParse != null && directParse.OriginalQuery != originalQuery)
            {
                return directParse with { OriginalQuery = originalQuery };
            }
            if (directParse != null)
            {
                return directParse;
            }

            // Fallback: create basic result
            return new QueryOptimizationResult
            {
                OriginalQuery = originalQuery,
                OptimizedQuery = response,
                Suggestions = new List<OptimizationSuggestion>(),
                ConfidenceScore = 50
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse optimization response, using fallback");
            return new QueryOptimizationResult
            {
                OriginalQuery = originalQuery,
                OptimizedQuery = response,
                Suggestions = new List<OptimizationSuggestion>(),
                ConfidenceScore = 50
            };
        }
    }
}
