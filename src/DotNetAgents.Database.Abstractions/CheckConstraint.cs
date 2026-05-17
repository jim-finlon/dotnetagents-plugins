namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a check constraint on a database table.
/// </summary>
public sealed class CheckConstraint
{
    /// <summary>
    /// Gets the name of the check constraint.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the check condition expression.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// Gets a value indicating whether the constraint is enabled.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the list of column names referenced in the check expression.
    /// </summary>
    public List<string> ReferencedColumns { get; init; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckConstraint"/> class.
    /// </summary>
    public CheckConstraint()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckConstraint"/> class.
    /// </summary>
    /// <param name="name">The constraint name.</param>
    /// <param name="expression">The check expression.</param>
    public CheckConstraint(string name, string expression)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    /// <summary>
    /// Returns a string representation of the check constraint.
    /// </summary>
    public override string ToString() => $"CHECK {Name}: {Expression}";
}
