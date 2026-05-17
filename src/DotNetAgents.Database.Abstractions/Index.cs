namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents an index on a database table.
/// </summary>
public sealed class Index
{
    /// <summary>
    /// Gets the name of the index.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the list of column names that comprise the index.
    /// </summary>
    public required List<string> ColumnNames { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the index is unique.
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// Gets a value indicating whether the index is clustered.
    /// </summary>
    public bool IsClustered { get; init; }

    /// <summary>
    /// Gets the type of the index (e.g., BTREE, HASH, etc.).
    /// </summary>
    public string? IndexType { get; init; }

    /// <summary>
    /// Gets the filter condition for partial indexes.
    /// </summary>
    public string? FilterCondition { get; init; }

    /// <summary>
    /// Gets a list of included columns (for covering indexes).
    /// </summary>
    public List<string> IncludedColumns { get; init; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Index"/> class.
    /// </summary>
    public Index()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Index"/> class.
    /// </summary>
    /// <param name="name">The index name.</param>
    /// <param name="columnNames">The column names.</param>
    /// <param name="isUnique">Whether the index is unique.</param>
    public Index(string name, IEnumerable<string> columnNames, bool isUnique = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ColumnNames = columnNames?.ToList() ?? throw new ArgumentNullException(nameof(columnNames));
        IsUnique = isUnique;
    }

    /// <summary>
    /// Returns a string representation of the index.
    /// </summary>
    public override string ToString()
    {
        var type = IsUnique ? "UNIQUE" : "INDEX";
        return $"{type} {Name} ({string.Join(", ", ColumnNames)})";
    }
}
