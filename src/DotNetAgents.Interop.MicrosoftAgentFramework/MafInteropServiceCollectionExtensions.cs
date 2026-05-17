using DotNetAgents.A2A;
using DotNetAgents.A2A.Client;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Interop.MicrosoftAgentFramework;

/// <summary>DI registration helpers for MAF interop. Story 68eb40ff.</summary>
public static class MafInteropServiceCollectionExtensions
{
    /// <summary>
    /// Register a remote MAF agent (one that exposes an A2A 1.0 server endpoint) as a
    /// DNA <see cref="IA2AAgent"/> via the A2A client. The DNA host then treats the MAF
    /// agent as just-another-A2A-endpoint.
    /// </summary>
    public static IServiceCollection AddMafA2AAgentAdapter(
        this IServiceCollection services,
        string localAgentId,
        string localDisplayName,
        Uri mafBaseUrl,
        A2AClientCallOptions? callOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(localAgentId);
        ArgumentNullException.ThrowIfNull(mafBaseUrl);

        services.AddSingleton<IA2AAgent>(sp =>
        {
            var client = sp.GetRequiredService<IA2AClient>();
            return new MafA2AAgentAdapter(client, mafBaseUrl, localAgentId, localDisplayName, callOptions);
        });
        return services;
    }

    /// <summary>
    /// Register an in-process <see cref="IMafAgentInvoker"/> for direct C# invocation of a
    /// MAF agent (no HTTP/A2A round-trip). The host supplies the implementation that
    /// references <c>Microsoft.Agents.AI</c> directly.
    /// </summary>
    public static IServiceCollection AddMafAgentInvoker(
        this IServiceCollection services,
        Func<IServiceProvider, IMafAgentInvoker> factory)
    {
        services.AddSingleton(factory);
        return services;
    }
}
