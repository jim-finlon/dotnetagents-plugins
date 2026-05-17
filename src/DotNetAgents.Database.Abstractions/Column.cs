namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database column with its metadata and properties.
/// This entity captures all necessary information for database schema analysis and operations.
/// </summary>
public sealed class Column
{
    /// <summary>
    /// Gets the name of the column.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the data type of the column (e.g., 'varchar', 'int', 'datetime').
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Gets the maximum length for character-based data types.
    /// Null for non-character types or when not applicable.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the precision for numeric data types.
    /// Represents the total number of digits.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// Gets the scale for numeric data types.
    /// Represents the number of digits after the decimal point.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    /// Gets a value indicating whether the column allows NULL values.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Gets the default value expression for the column.
    /// Null if no default value is defined.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether this column is an identity/auto-increment column.
    /// </summary>
    public bool IsIdentity { get; init; }

    /// <summary>
    /// Gets the seed value for identity columns.
    /// Null if the column is not an identity column.
    /// </summary>
    public long? IdentitySeed { get; init; }

    /// <summary>
    /// Gets the increment value for identity columns.
    /// Null if the column is not an identity column.
    /// </summary>
    public long? IdentityIncrement { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Column"/> class.
    /// </summary>
    public Column()
    {
        // Parameterless constructor for serialization and object initialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Column"/> class with required properties.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="dataType">The column data type.</param>
    /// <param name="isNullable">Whether the column allows null values.</param>
    public Column(string name, string dataType, bool isNullable = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        IsNullable = isNullable;
    }

    /// <summary>
    /// Gets a value indicating whether this column has numeric precision/scale properties.
    /// </summary>
    public bool HasNumericPrecision => Precision.HasValue || Scale.HasValue;

    /// <summary>
    /// Gets a value indicating whether this column has character length properties.
    /// </summary>
    public bool HasCharacterLength => MaxLength.HasValue;

    /// <summary>
    /// Gets the display name for the column including type information.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var result = $"{Name} ({DataType}";

            if (HasCharacterLength)
                result += $"({MaxLength})";
            else if (HasNumericPrecision)
                result += $"({Precision}{(Scale.HasValue ? $",{Scale}" : "")})";

            result += IsNullable ? ", NULL)" : ", NOT NULL)";

            if (IsIdentity)
                result += " IDENTITY";

            return result;
        }
    }

    /// <summary>
    /// Validates the column properties for consistency.
    /// </summary>
    /// <returns>True if the column is valid; otherwise, false.</returns>
    public bool IsValid()
    {
        // Basic validation rules
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(DataType))
            return false;

        // Identity columns should have seed and increment values
        if (IsIdentity && (!IdentitySeed.HasValue || !IdentityIncrement.HasValue))
            return false;

        // Non-identity columns should not have identity properties
        if (!IsIdentity && (IdentitySeed.HasValue || IdentityIncrement.HasValue))
            return false;

        // Scale cannot be greater than precision for numeric types
        if (Precision.HasValue && Scale.HasValue && Scale > Precision)
            return false;

        return true;
    }

    /// <summary>
    /// Returns a string representation of the column.
    /// </summary>
    /// <returns>A string that represents the current column.</returns>
    public override string ToString() => DisplayName;

    /// <summary>
    /// Determines whether the specified object is equal to the current column.
    /// </summary>
    /// <param name="obj">The object to compare with the current column.</param>
    /// <returns>True if the specified object is equal to the current column; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Column other &&
               Name == other.Name &&
               DataType == other.DataType &&
               MaxLength == other.MaxLength &&
               Precision == other.Precision &&
               Scale == other.Scale &&
               IsNullable == other.IsNullable &&
               DefaultValue == other.DefaultValue &&
               IsIdentity == other.IsIdentity &&
               IdentitySeed == other.IdentitySeed &&
               IdentityIncrement == other.IdentityIncrement;
    }

    /// <summary>
    /// Returns the hash code for this column.
    /// </summary>
    /// <returns>A hash code for the current column.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(DataType);
        hash.Add(MaxLength);
        hash.Add(Precision);
        hash.Add(Scale);
        hash.Add(IsNullable);
        hash.Add(DefaultValue);
        hash.Add(IsIdentity);
        hash.Add(IdentitySeed);
        hash.Add(IdentityIncrement);
        return hash.ToHashCode();
    }
}
