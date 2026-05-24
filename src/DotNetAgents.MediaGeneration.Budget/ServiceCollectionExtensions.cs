using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>DI registration helpers for <c>DotNetAgents.MediaGeneration.Budget</c>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register public budget defaults:
    /// <list type="bullet">
    ///   <item><see cref="IBudgetLedger"/> → <see cref="InMemoryBudgetLedger"/> (singleton).</item>
    ///   <item><see cref="ICostEstimator"/> → <see cref="HistoricalMeanCostEstimator"/> (singleton).</item>
    ///   <item><see cref="IBudgetGuard"/> is intentionally not registered by the public package.</item>
    ///   <item><see cref="VideoBudgetOptions"/> bound from configuration.</item>
    /// </list>
    /// Production composition (P7.5 follow-up) overrides the ledger with an EF-backed impl.
    /// </summary>
    public static IServiceCollection AddMediaGenerationBudget(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = VideoBudgetOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<VideoBudgetOptions>()
            .Bind(configuration.GetSection(sectionName));

        services.TryAddSingleton<IBudgetLedger, InMemoryBudgetLedger>();
        services.TryAddSingleton<ICostEstimator, HistoricalMeanCostEstimator>();

        return services;
    }
}
