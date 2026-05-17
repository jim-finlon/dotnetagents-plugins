namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a default constraint on a database table column.
/// </summary>
public sealed class DefaultConstraint
{
    /// <summary>
    /// Gets the name of the default constraint.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the column this default constraint applies to.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// Gets the default value expression.
    /// </summary>
    public required string DefaultValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a system-generated constraint name.
    /// </summary>
    public bool IsSystemGenerated { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConstraint"/> class.
    /// </summary>
    public DefaultConstraint()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConstraint"/> class.
    /// </summary>
    /// <param name="name">The constraint name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="defaultValue">The default value expression.</param>
    public DefaultConstraint(string name, string columnName, string defaultValue)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
    }

    /// <summary>
    /// Returns a string representation of the default constraint.
    /// </summary>
    public override string ToString() => $"DEFAULT {Name}: {ColumnName} = {DefaultValue}";
}
