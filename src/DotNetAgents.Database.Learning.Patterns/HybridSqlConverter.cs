using DotNetAgents.Database.Dialects;

namespace DotNetAgents.Database.Learning.Patterns;

public interface IHybridSqlConverter
{
    Task<SqlConversionResult> ConvertAsync(
        string sql,
        string sourceDialect,
        string targetDialect,
        CancellationToken cancellationToken = default);

    SqlComplexityAnalysis AnalyzeComplexity(string sql, string sourceDialect, string targetDialect);
}

public sealed class HybridSqlConverter : IHybridSqlConverter
{
    private const double ConfidenceThreshold = 0.7;
    private const double AutoApplyThreshold = 0.85;

    private readonly IDbDialectFactory _dialectFactory;
    private readonly ISqlPatternNormalizer _normalizer;
    private readonly ISqlPatternStore? _patternStore;

    public HybridSqlConverter(
        IDbDialectFactory dialectFactory,
        ISqlPatternNormalizer normalizer,
        ISqlPatternStore? patternStore = null)
    {
        _dialectFactory = dialectFactory ?? throw new ArgumentNullException(nameof(dialectFactory));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _patternStore = patternStore;
    }

    public async Task<SqlConversionResult> ConvertAsync(
        string sql,
        string sourceDialect,
        string targetDialect,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlConversionResult
            {
                IsSuccessful = true,
                ConvertedSql = sql,
                Confidence = 1.0,
                RequiresAIReview = false
            };
        }

        var normalized = _normalizer.NormalizeSql(sql);
        if (_patternStore is not null)
        {
            var patterns = await _patternStore.ListAsync(
                new SqlPatternQuery(
                    TagFilters: new Dictionary<string, string>
                    {
                        ["SourceDialect"] = sourceDialect,
                        ["TargetDialect"] = targetDialect
                    },
                    Take: 10),
                cancellationToken).ConfigureAwait(false);

            var matchingPattern = patterns.FirstOrDefault(pattern =>
                string.Equals(pattern.Pattern.PatternHash, normalized.PatternHash, StringComparison.Ordinal));

            if (matchingPattern is not null && TryGetConfidence(matchingPattern, out var confidence) && confidence >= ConfidenceThreshold)
            {
                return new SqlConversionResult
                {
                    IsSuccessful = true,
                    ConvertedSql = ApplyPattern(sql, matchingPattern, normalized),
                    Confidence = confidence,
                    RequiresAIReview = confidence < AutoApplyThreshold,
                    TransformationsApplied = [$"Applied cached pattern (id: {matchingPattern.PatternId}, confidence: {confidence:F2})"]
                };
            }
        }

        var targetDialectImpl = _dialectFactory.GetDialect(targetDialect);
        var convertedSql = targetDialectImpl.ConvertSql(sql);
        var canConvert = targetDialectImpl.CanConvertWithRules(sql);
        var unsupported = targetDialectImpl.GetUnsupportedConstructs(sql).ToList();
        var confidenceScore = CalculateRuleConfidence(sql, convertedSql, unsupported, canConvert);

        if (canConvert && confidenceScore >= ConfidenceThreshold)
        {
            var result = new SqlConversionResult
            {
                IsSuccessful = true,
                ConvertedSql = convertedSql,
                Confidence = confidenceScore,
                RequiresAIReview = confidenceScore < AutoApplyThreshold,
                TransformationsApplied = ["Applied rule-based conversion"],
                Warnings = unsupported
                    .Where(construct => construct.Severity == ConstructSeverity.Warning)
                    .Select(construct => $"{construct.ConstructType}: {construct.Suggestion}")
                    .ToList()
            };

            if (_patternStore is not null)
            {
                await _patternStore.UpsertAsync(
                    CreateRecord(result, normalized, sourceDialect, targetDialect, "Rule"),
                    cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        return new SqlConversionResult
        {
            IsSuccessful = !unsupported.Any(construct => construct.Severity == ConstructSeverity.Error),
            ConvertedSql = convertedSql,
            Confidence = Math.Max(0.3, confidenceScore),
            RequiresAIReview = true,
            TransformationsApplied = canConvert ? ["Applied partial rule-based conversion"] : [],
            Warnings = unsupported.Select(construct => $"{construct.ConstructType}: {construct.Suggestion ?? "Manual review required"}").ToList(),
            UnsupportedConstructs = unsupported
        };
    }

    public SqlComplexityAnalysis AnalyzeComplexity(string sql, string sourceDialect, string targetDialect)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlComplexityAnalysis
            {
                Complexity = ComplexityLevel.Simple,
                CanConvertWithRules = true,
                RequiresAI = false,
                EstimatedConfidence = 1.0,
                UnsupportedConstructs = [],
                Recommendations = []
            };
        }

        var targetDialectImpl = _dialectFactory.GetDialect(targetDialect);
        var canConvert = targetDialectImpl.CanConvertWithRules(sql);
        var unsupported = targetDialectImpl.GetUnsupportedConstructs(sql).ToList();
        var complexity = unsupported.Count switch
        {
            0 => ComplexityLevel.Simple,
            1 or 2 when unsupported.All(construct => construct.Severity == ConstructSeverity.Warning) => ComplexityLevel.Moderate,
            >= 3 and < 5 => ComplexityLevel.Complex,
            _ => ComplexityLevel.VeryComplex
        };

        if (sql.Contains("CURSOR", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("DYNAMIC", StringComparison.OrdinalIgnoreCase))
        {
            complexity = ComplexityLevel.VeryComplex;
        }

        var recommendations = new List<string>
        {
            complexity switch
            {
                ComplexityLevel.Simple => "This SQL can be converted automatically using rules.",
                ComplexityLevel.Moderate => "Rule-based conversion will work, but review the output.",
                _ => "AI assistance is recommended for this conversion."
            }
        };

        recommendations.AddRange(unsupported
            .Where(construct => !string.IsNullOrEmpty(construct.Suggestion))
            .Select(construct => $"{construct.ConstructType}: {construct.Suggestion}")
            .Distinct());

        return new SqlComplexityAnalysis
        {
            Complexity = complexity,
            CanConvertWithRules = canConvert,
            RequiresAI = !canConvert || complexity >= ComplexityLevel.Complex,
            EstimatedConfidence = canConvert
                ? Math.Max(0.3, 1.0 - (unsupported.Count * 0.15))
                : 0.3,
            UnsupportedConstructs = unsupported,
            Recommendations = recommendations
        };
    }

    private SqlPatternRecord CreateRecord(
        SqlConversionResult result,
        NormalizedSqlPattern normalized,
        string sourceDialect,
        string targetDialect,
        string source)
    {
        return new SqlPatternRecord(
            PatternId: normalized.PatternHash,
            Pattern: normalized,
            Complexity: AnalyzeComplexity(result.ConvertedSql, sourceDialect, targetDialect),
            Tags: new Dictionary<string, string>
            {
                ["SourceDialect"] = sourceDialect,
                ["TargetDialect"] = targetDialect,
                ["Source"] = source,
                ["Confidence"] = result.Confidence.ToString("F2")
            },
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Source: source);
    }

    private static bool TryGetConfidence(SqlPatternRecord record, out double confidence)
    {
        if (record.Tags.TryGetValue("Confidence", out var value) &&
            double.TryParse(value, out confidence))
        {
            return true;
        }

        confidence = 0.5;
        return false;
    }

    private static double CalculateRuleConfidence(
        string original,
        string converted,
        List<UnsupportedConstruct> unsupported,
        bool canConvert)
    {
        var confidence = 1.0;

        foreach (var construct in unsupported)
        {
            confidence -= construct.Severity switch
            {
                ConstructSeverity.Error => 0.3,
                ConstructSeverity.Warning => 0.1,
                ConstructSeverity.Info => 0.02,
                _ => 0.1
            };
        }

        if (original == converted && unsupported.Count == 0)
        {
            confidence = !original.Contains('[') &&
                !original.Contains("GETDATE", StringComparison.OrdinalIgnoreCase) &&
                !original.Contains("ISNULL", StringComparison.OrdinalIgnoreCase)
                    ? 0.95
                    : 0.5;
        }

        if (!canConvert)
        {
            confidence = Math.Min(confidence, 0.5);
        }

        return Math.Max(0.0, Math.Min(1.0, confidence));
    }

    private static string ApplyPattern(string originalSql, SqlPatternRecord pattern, NormalizedSqlPattern normalized)
    {
        var result = pattern.Tags.TryGetValue("Solution", out var solution) ? solution : originalSql;

        foreach (var (placeholder, extracted) in normalized.ExtractedValues)
        {
            var convertedValue = extracted.Type switch
            {
                PlaceholderType.StringLiteral when extracted.OriginalValue.StartsWith("N'", StringComparison.OrdinalIgnoreCase)
                    => extracted.OriginalValue[1..],
                PlaceholderType.Variable => extracted.OriginalValue.TrimStart('@'),
                _ => extracted.OriginalValue
            };

            result = result.Replace(placeholder, convertedValue, StringComparison.Ordinal);
        }

        return result;
    }
}

public sealed class SqlComplexityAnalysis
{
    public required ComplexityLevel Complexity { get; init; }

    public required bool CanConvertWithRules { get; init; }

    public required bool RequiresAI { get; init; }

    public required double EstimatedConfidence { get; init; }

    public List<UnsupportedConstruct> UnsupportedConstructs { get; init; } = [];

    public List<string> Recommendations { get; init; } = [];
}

public enum ComplexityLevel
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}
