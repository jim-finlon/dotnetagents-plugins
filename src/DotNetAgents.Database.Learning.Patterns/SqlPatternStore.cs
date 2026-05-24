using System.Collections.Concurrent;
using System.Text.Json;

namespace DotNetAgents.Database.Learning.Patterns;

public interface ISqlPatternStore
{
    ValueTask<IReadOnlyList<SqlPatternRecord>> ListAsync(
        SqlPatternQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<SqlPatternRecord?> GetAsync(
        string patternId,
        CancellationToken cancellationToken = default);

    ValueTask UpsertAsync(
        SqlPatternRecord record,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(
        string patternId,
        CancellationToken cancellationToken = default);
}

public sealed record SqlPatternRecord(
    string PatternId,
    NormalizedSqlPattern Pattern,
    SqlComplexityAnalysis Complexity,
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset CapturedAtUtc,
    string Source);

public sealed record SqlPatternQuery(
    string? DialectId = null,
    ComplexityLevel? MinComplexity = null,
    IReadOnlyDictionary<string, string>? TagFilters = null,
    int? Take = null);

public interface ISqlPatternExtractor
{
    ValueTask<IReadOnlyList<SqlPatternRecord>> ExtractAsync(
        string sql,
        CancellationToken cancellationToken = default);
}

public sealed class InMemorySqlPatternStore : ISqlPatternStore
{
    private readonly ConcurrentDictionary<string, SqlPatternRecord> _records = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<SqlPatternRecord>> ListAsync(
        SqlPatternQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<SqlPatternRecord> records = _records.Values;

        if (!string.IsNullOrWhiteSpace(query.DialectId))
        {
            records = records.Where(record =>
                record.Tags.TryGetValue("DialectId", out var dialectId) &&
                string.Equals(dialectId, query.DialectId, StringComparison.OrdinalIgnoreCase));
        }

        if (query.MinComplexity is { } minComplexity)
        {
            records = records.Where(record => record.Complexity.Complexity >= minComplexity);
        }

        if (query.TagFilters is { Count: > 0 })
        {
            records = records.Where(record =>
                query.TagFilters.All(filter =>
                    record.Tags.TryGetValue(filter.Key, out var value) &&
                    string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase)));
        }

        records = records.OrderByDescending(record => record.CapturedAtUtc);

        if (query.Take is > 0)
        {
            records = records.Take(query.Take.Value);
        }

        return ValueTask.FromResult<IReadOnlyList<SqlPatternRecord>>(records.ToList());
    }

    public ValueTask<SqlPatternRecord?> GetAsync(
        string patternId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryGetValue(patternId, out var record);
        return ValueTask.FromResult(record);
    }

    public ValueTask UpsertAsync(
        SqlPatternRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records[record.PatternId] = record;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(
        string patternId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryRemove(patternId, out _);
        return ValueTask.CompletedTask;
    }
}

public sealed class JsonFileSqlPatternStore : ISqlPatternStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonFileSqlPatternStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("Pattern store path is required.", nameof(path))
            : path;
    }

    public async ValueTask<IReadOnlyList<SqlPatternRecord>> ListAsync(
        SqlPatternQuery query,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var memory = new InMemorySqlPatternStore();
        foreach (var record in records)
        {
            await memory.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return await memory.ListAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SqlPatternRecord?> GetAsync(
        string patternId,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return records.FirstOrDefault(record => string.Equals(record.PatternId, patternId, StringComparison.Ordinal));
    }

    public async ValueTask UpsertAsync(
        SqlPatternRecord record,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var index = records.FindIndex(existing => string.Equals(existing.PatternId, record.PatternId, StringComparison.Ordinal));
        if (index >= 0)
        {
            records[index] = record;
        }
        else
        {
            records.Add(record);
        }

        await SaveAsync(records, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(
        string patternId,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadAsync(cancellationToken).ConfigureAwait(false);
        records.RemoveAll(record => string.Equals(record.PatternId, patternId, StringComparison.Ordinal));
        await SaveAsync(records, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<SqlPatternRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        var format = await JsonSerializer.DeserializeAsync<SqlPatternJsonFormat>(
            stream,
            _serializerOptions,
            cancellationToken).ConfigureAwait(false);

        return format?.Patterns ?? [];
    }

    private async Task SaveAsync(List<SqlPatternRecord> records, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var format = new SqlPatternJsonFormat
        {
            Version = "1.0",
            ExportedAt = DateTimeOffset.UtcNow,
            ExportedFrom = nameof(JsonFileSqlPatternStore),
            Patterns = records
        };

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(
            stream,
            format,
            _serializerOptions,
            cancellationToken).ConfigureAwait(false);
    }
}
