using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// Extension methods for registering database analysis services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SQL Server schema analyzer to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlServerSchemaAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaAnalyzer, SqlServerSchemaAnalyzer>();
        return services;
    }

    /// <summary>
    /// Adds PostgreSQL schema analyzer to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSQLSchemaAnalyzer(this IServiceCollection services)
    {
        services.AddSingleton<ISchemaAnalyzer, PostgreSQLSchemaAnalyzer>();
        return services;
    }

    /// <summary>
    /// Adds both SQL Server and PostgreSQL schema analyzers to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseSchemaAnalyzers(this IServiceCollection services)
    {
        services.AddSqlServerSchemaAnalyzer();
        services.AddPostgreSQLSchemaAnalyzer();
        return services;
    }

    /// <summary>
    /// Adds the schema analyzer factory to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemaAnalyzerFactory(this IServiceCollection services)
    {
        services.AddSingleton<SchemaAnalyzerFactory>();
        return services;
    }
}
