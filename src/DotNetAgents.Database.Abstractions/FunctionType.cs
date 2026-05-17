namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents the type of database function.
/// </summary>
public enum FunctionType
{
    /// <summary>
    /// Scalar function that returns a single value.
    /// </summary>
    Scalar = 0,

    /// <summary>
    /// Table-valued function that returns a table.
    /// </summary>
    TableValued = 1,

    /// <summary>
    /// Aggregate function that operates on a set of values.
    /// </summary>
    Aggregate = 2,

    /// <summary>
    /// Window function that operates over a partition of rows.
    /// </summary>
    Window = 3
}
