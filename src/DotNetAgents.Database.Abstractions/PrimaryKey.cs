namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a primary key constraint on a database table.
/// </summary>
public sealed class PrimaryKey
{
    /// <summary>
    /// Gets the name of the primary key constraint.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the list of column names that comprise the primary key.
    /// </summary>
    public required List<string> ColumnNames { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the primary key is clustered.
    /// </summary>
    public bool IsClustered { get; init; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKey"/> class.
    /// </summary>
    public PrimaryKey()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKey"/> class.
    /// </summary>
    /// <param name="name">The constraint name.</param>
    /// <param name="columnNames">The column names.</param>
    public PrimaryKey(string name, params string[] columnNames)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ColumnNames = columnNames?.ToList() ?? throw new ArgumentNullException(nameof(columnNames));
    }

    /// <summary>
    /// Returns a string representation of the primary key.
    /// </summary>
    public override string ToString() => $"PK_{Name} ({string.Join(", ", ColumnNames)})";
}
