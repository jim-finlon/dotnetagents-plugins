namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents the direction of a parameter.
/// </summary>
public enum ParameterDirection
{
    /// <summary>
    /// Input parameter (default).
    /// </summary>
    Input = 0,

    /// <summary>
    /// Output parameter.
    /// </summary>
    Output = 1,

    /// <summary>
    /// Input/Output parameter.
    /// </summary>
    InputOutput = 2
}
