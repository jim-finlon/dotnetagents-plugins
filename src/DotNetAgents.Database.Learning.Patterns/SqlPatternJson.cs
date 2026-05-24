using System.Text.Json;

namespace DotNetAgents.Database.Learning.Patterns;

public interface ISqlPatternJsonExporter
{
    Task ExportAsync(
        ISqlPatternStore store,
        string outputPath,
        SqlPatternQuery? query = null,
        CancellationToken cancellationToken = default);
}

public interface ISqlPatternJsonImporter
{
    Task ImportAsync(
        ISqlPatternStore store,
        string inputPath,
        CancellationToken cancellationToken = default);
}

public sealed class SqlPatternJsonFormat
{
    public required string Version { get; init; } = "1.0";

    public required DateTimeOffset ExportedAt { get; init; }

    public required string ExportedFrom { get; init; }

    public required List<SqlPatternRecord> Patterns { get; init; }
}

public sealed class SqlPatternJsonItem
{
    public required string PatternId { get; init; }

    public required NormalizedSqlPattern Pattern { get; init; }

    public required SqlComplexityAnalysis Complexity { get; init; }

    public required Dictionary<string, string> Tags { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required string Source { get; init; }
}

public sealed class SqlPatternJsonExporter : ISqlPatternJsonExporter
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task ExportAsync(
        ISqlPatternStore store,
        string outputPath,
        SqlPatternQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var records = await store.ListAsync(query ?? new SqlPatternQuery(), cancellationToken).ConfigureAwait(false);
        var format = new SqlPatternJsonFormat
        {
            Version = "1.0",
            ExportedAt = DateTimeOffset.UtcNow,
            ExportedFrom = store.GetType().Name,
            Patterns = records.ToList()
        };

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(outputPath);
        await JsonSerializer.SerializeAsync(
            stream,
            format,
            _serializerOptions,
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class SqlPatternJsonImporter : ISqlPatternJsonImporter
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task ImportAsync(
        ISqlPatternStore store,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Pattern file not found.", inputPath);
        }

        await using var stream = File.OpenRead(inputPath);
        var format = await JsonSerializer.DeserializeAsync<SqlPatternJsonFormat>(
            stream,
            _serializerOptions,
            cancellationToken).ConfigureAwait(false);

        if (format is null)
        {
            throw new InvalidOperationException("Failed to deserialize SQL pattern JSON.");
        }

        foreach (var pattern in format.Patterns)
        {
            await store.UpsertAsync(pattern, cancellationToken).ConfigureAwait(false);
        }
    }
}
