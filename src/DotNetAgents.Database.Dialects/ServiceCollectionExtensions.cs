using DotNetAgents.Database.Dialects.Dialects;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Database.Dialects;

/// <summary>
/// Extension methods for registering database dialect services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds database dialect services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="sourceDialect">The source database dialect (e.g., "MSSQL").</param>
    /// <param name="targetDialect">The target database dialect (e.g., "PostgreSQL").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseDialects(
        this IServiceCollection services,
        string sourceDialect = "MSSQL",
        string targetDialect = "PostgreSQL")
    {
        // Register dialect implementations
        services.AddSingleton<IDbDialect, MsSqlDialect>(sp => new MsSqlDialect());
        services.AddSingleton<IDbDialect, PostgreSqlDialect>(sp => new PostgreSqlDialect());

        // Register dialect factory
        services.AddSingleton<IDbDialectFactory>(sp =>
        {
            var dialects = sp.GetServices<IDbDialect>().ToList();
            return new DbDialectFactory(dialects);
        });

        return services;
    }
}

/// <summary>
/// Factory for creating database dialect instances.
/// </summary>
public interface IDbDialectFactory
{
    /// <summary>
    /// Gets a dialect by database type.
    /// </summary>
    /// <param name="databaseType">The database type (e.g., "MSSQL", "PostgreSQL").</param>
    /// <returns>The dialect instance.</returns>
    IDbDialect GetDialect(string databaseType);
}

/// <summary>
/// Implementation of database dialect factory.
/// </summary>
public sealed class DbDialectFactory : IDbDialectFactory
{
    private readonly Dictionary<string, IDbDialect> _dialects;

    public DbDialectFactory(IEnumerable<IDbDialect> dialects)
    {
        _dialects = dialects.ToDictionary(d => d.DatabaseType, StringComparer.OrdinalIgnoreCase);
    }

    public IDbDialect GetDialect(string databaseType)
    {
        if (_dialects.TryGetValue(databaseType, out var dialect))
        {
            return dialect;
        }

        throw new ArgumentException($"No dialect found for database type: {databaseType}", nameof(databaseType));
    }
}
