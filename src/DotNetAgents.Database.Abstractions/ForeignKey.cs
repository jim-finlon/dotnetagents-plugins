namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a foreign key constraint on a database table.
/// </summary>
public sealed class ForeignKey
{
    /// <summary>
    /// Gets the name of the foreign key constraint.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the list of column names in the source table that participate in the foreign key.
    /// </summary>
    public required List<string> ColumnNames { get; init; } = [];

    /// <summary>
    /// Gets the name of the referenced table schema.
    /// </summary>
    public required string ReferencedSchemaName { get; init; }

    /// <summary>
    /// Gets the name of the referenced table.
    /// </summary>
    public required string ReferencedTableName { get; init; }

    /// <summary>
    /// Gets the list of column names in the referenced table.
    /// </summary>
    public required List<string> ReferencedColumnNames { get; init; } = [];

    /// <summary>
    /// Gets the action to take when the referenced key is updated.
    /// </summary>
    public string? OnUpdate { get; init; }

    /// <summary>
    /// Gets the action to take when the referenced key is deleted.
    /// </summary>
    public string? OnDelete { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignKey"/> class.
    /// </summary>
    public ForeignKey()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Returns a string representation of the foreign key.
    /// </summary>
    public override string ToString() =>
        $"FK_{Name}: {string.Join(", ", ColumnNames)} -> {ReferencedSchemaName}.{ReferencedTableName}({string.Join(", ", ReferencedColumnNames)})";
}
