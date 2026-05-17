using DotNetAgents.Abstractions.Models;
using DotNetAgents.Database.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotNetAgents.Database.AI;

/// <summary>
/// AI-powered stored procedure converter between database systems.
/// </summary>
public sealed class AIProcedureConverter
{
    private readonly ILLMModel<string, string> _llm;
    private readonly ILogger<AIProcedureConverter>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProcedureConverter"/> class.
    /// </summary>
    /// <param name="llm">The LLM model to use for conversion.</param>
    /// <param name="logger">Optional logger instance.</param>
    public AIProcedureConverter(
        ILLMModel<string, string> llm,
        ILogger<AIProcedureConverter>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    /// <summary>
    /// Converts a stored procedure from one database system to another.
    /// </summary>
    /// <param name="procedure">The stored procedure to convert.</param>
    /// <param name="targetDatabase">The target database system (e.g., "PostgreSQL", "SQL Server").</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The converted procedure definition.</returns>
    public async Task<ProcedureConversionResult> ConvertAsync(
        StoredProcedure procedure,
        string targetDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabase);

        _logger?.LogInformation("Converting stored procedure {ProcedureName} to {TargetDatabase}", procedure.ProcedureName, targetDatabase);

        try
        {
            var prompt = BuildConversionPrompt(procedure, targetDatabase);

            var options = new LLMOptions
            {
                Temperature = 0.3,
                MaxTokens = 4000
            };

            var response = await _llm.GenerateAsync(prompt, options, cancellationToken).ConfigureAwait(false);

            return ParseConversionResponse(response, procedure);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during procedure conversion");
            throw;
        }
    }

    private static string BuildConversionPrompt(StoredProcedure procedure, string targetDatabase)
    {
        var prompt = $@"You are an expert database migration specialist. Convert the following stored procedure to {targetDatabase} syntax.

Original Procedure:
Schema: {procedure.SchemaName}
Name: {procedure.ProcedureName}
Definition:
{procedure.Definition}

Parameters: {procedure.ParameterCount}

Convert this procedure to {targetDatabase} syntax, handling:
- Syntax differences
- Function/operator mappings
- Error handling patterns
- Transaction management
- Return value handling

Return the converted procedure as JSON:
{{
  ""converted_definition"": ""converted SQL"",
  ""conversion_notes"": [""note1"", ""note2""],
  ""confidence_score"": 85,
  ""warnings"": [""warning1""]
}}";

        return prompt;
    }

    private ProcedureConversionResult ParseConversionResponse(string response, StoredProcedure procedure)
    {
        try
        {
            // Try to parse as JSON first
            var jsonMatch = Regex.Match(response, @"```json\s*(\{.*?\})\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Groups[1].Value;
                var parsed = JsonSerializer.Deserialize<ProcedureConversionResult>(json);
                if (parsed != null)
                {
                    return parsed with { OriginalProcedureName = procedure.ProcedureName };
                }
            }

            // Try direct JSON parse
            var directParse = JsonSerializer.Deserialize<ProcedureConversionResult>(response);
            if (directParse != null && directParse.OriginalProcedureName != procedure.ProcedureName)
            {
                return directParse with { OriginalProcedureName = procedure.ProcedureName };
            }
            if (directParse != null)
            {
                return directParse;
            }

            // Fallback: use response as converted definition
            return new ProcedureConversionResult
            {
                OriginalProcedureName = procedure.ProcedureName,
                ConvertedDefinition = response,
                ConversionNotes = new List<string>(),
                ConfidenceScore = 50,
                Warnings = new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse conversion response, using fallback");
            return new ProcedureConversionResult
            {
                OriginalProcedureName = procedure.ProcedureName,
                ConvertedDefinition = response,
                ConversionNotes = new List<string>(),
                ConfidenceScore = 50,
                Warnings = new List<string>()
            };
        }
    }
}

/// <summary>
/// Result of a stored procedure conversion operation.
/// </summary>
public sealed record ProcedureConversionResult
{
    /// <summary>
    /// Gets the original procedure name.
    /// </summary>
    public required string OriginalProcedureName { get; init; }

    /// <summary>
    /// Gets the converted procedure definition.
    /// </summary>
    public required string ConvertedDefinition { get; init; }

    /// <summary>
    /// Gets conversion notes.
    /// </summary>
    public List<string> ConversionNotes { get; init; } = [];

    /// <summary>
    /// Gets confidence score (0-100).
    /// </summary>
    public int ConfidenceScore { get; init; }

    /// <summary>
    /// Gets any warnings about the conversion.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}
