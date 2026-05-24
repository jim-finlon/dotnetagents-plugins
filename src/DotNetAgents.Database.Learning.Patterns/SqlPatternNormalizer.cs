using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetAgents.Database.Learning.Patterns;

public interface ISqlPatternNormalizer
{
    NormalizedSqlPattern NormalizeSql(string sql);
}

public sealed class NormalizedSqlPattern
{
    public required string Pattern { get; init; }

    public required string PatternHash { get; init; }

    public required Dictionary<string, ExtractedValue> ExtractedValues { get; init; }
}

public sealed class ExtractedValue
{
    public required string Placeholder { get; init; }

    public required string OriginalValue { get; init; }

    public required PlaceholderType Type { get; init; }

    public int Position { get; init; }
}

public enum PlaceholderType
{
    StringLiteral,
    NumericLiteral,
    TableName,
    ColumnName,
    SchemaName,
    Variable,
    Parameter,
    DateTimeLiteral
}

public sealed class SqlPatternNormalizer : ISqlPatternNormalizer
{
    private static readonly Regex StringLiteralRegex = new(@"N?'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex NumericLiteralRegex = new(@"\b\d+(?:\.\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex IdentifierRegex = new(@"(?:\[([^\]]+)\]|""([^""]+)"")", RegexOptions.Compiled);
    private static readonly Regex VariableRegex = new(@"@\w+", RegexOptions.Compiled);
    private static readonly Regex DateLiteralRegex = new(@"'\d{4}-\d{2}-\d{2}(?:\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)?'", RegexOptions.Compiled);

    public NormalizedSqlPattern NormalizeSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new NormalizedSqlPattern
            {
                Pattern = sql,
                PatternHash = ComputeHash(sql),
                ExtractedValues = []
            };
        }

        var extracted = new Dictionary<string, ExtractedValue>();
        var normalized = sql;
        var placeholderIndex = 0;

        normalized = DateLiteralRegex.Replace(normalized, match =>
        {
            var placeholder = $"#DATE{placeholderIndex++}#";
            extracted[placeholder] = new ExtractedValue
            {
                Placeholder = placeholder,
                OriginalValue = match.Value,
                Type = PlaceholderType.DateTimeLiteral,
                Position = match.Index
            };
            return placeholder;
        });

        normalized = StringLiteralRegex.Replace(normalized, match =>
        {
            var placeholder = $"#STR{placeholderIndex++}#";
            extracted[placeholder] = new ExtractedValue
            {
                Placeholder = placeholder,
                OriginalValue = match.Value,
                Type = PlaceholderType.StringLiteral,
                Position = match.Index
            };
            return placeholder;
        });

        normalized = NumericLiteralRegex.Replace(normalized, match =>
        {
            if (match.Value.StartsWith('#'))
            {
                return match.Value;
            }

            var placeholder = $"#NUM{placeholderIndex++}#";
            extracted[placeholder] = new ExtractedValue
            {
                Placeholder = placeholder,
                OriginalValue = match.Value,
                Type = PlaceholderType.NumericLiteral,
                Position = match.Index
            };
            return placeholder;
        });

        normalized = VariableRegex.Replace(normalized, match =>
        {
            var placeholder = $"#VAR{placeholderIndex++}#";
            extracted[placeholder] = new ExtractedValue
            {
                Placeholder = placeholder,
                OriginalValue = match.Value,
                Type = PlaceholderType.Variable,
                Position = match.Index
            };
            return placeholder;
        });

        normalized = IdentifierRegex.Replace(normalized, match =>
        {
            var identifier = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var placeholder = $"#ID{placeholderIndex++}#";
            extracted[placeholder] = new ExtractedValue
            {
                Placeholder = placeholder,
                OriginalValue = identifier,
                Type = PlaceholderType.TableName,
                Position = match.Index
            };
            return placeholder;
        });

        normalized = NormalizeWhitespace(normalized);

        return new NormalizedSqlPattern
        {
            Pattern = normalized,
            PatternHash = ComputeHash(normalized),
            ExtractedValues = extracted
        };
    }

    private static string NormalizeWhitespace(string sql)
    {
        sql = Regex.Replace(sql, @"\s+", " ");
        sql = sql.Replace("\r\n", "\n").Replace("\r", "\n");
        sql = string.Join("\n", sql.Split('\n').Select(line => line.Trim()));
        return sql.Trim();
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
