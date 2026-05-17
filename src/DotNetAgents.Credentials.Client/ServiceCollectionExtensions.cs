using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotNetAgents.A2A.Client;
using DotNetAgents.Abstractions.PublicSubstitutes.Credentials;

namespace DotNetAgents.Credentials.Client;

/// <summary>
/// Wire CredentialsClient into any .NET service with one call.
///
/// <example>
/// <code>
/// // appsettings.json:
/// // "CredentialsClient": { "BaseUrl": "http://credential-service:8080", "AgentId": "publishing-agent", "McpIngressApiKey": "optional-api-key" }
///
/// builder.Services.AddCredentialsClient(builder.Configuration);
///
/// // Anywhere:
/// public class Foo(ICredentialsClient credentials) {
///   public async Task Bar(CancellationToken ct) {
///     var token = await credentials.GetCredentialAsync("service/example/credential", "service_token", ct);
///     // use token.Value
///   }
/// }
/// </code>
/// </example>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="CredentialsClientOptions"/> from the <c>CredentialsClient</c> config section
    /// and registers <see cref="ICredentialsClient"/> with a named HttpClient.
    /// </summary>
    public static IServiceCollection AddCredentialsClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CredentialsClientOptions>()
            .Bind(configuration.GetSection(CredentialsClientOptions.SectionName))
            .ValidateOnStart();
        services.AddA2AClient();
        services.AddHttpClient(CredentialsClient.HttpClientName);
        services.AddSingleton<IWorkerAuthRevocationCache, InMemoryWorkerAuthRevocationCache>();
        services.AddSingleton<ICredentialsClient>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CredentialsClientOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CredentialsClient>>();
            return new CredentialsClient(
                httpFactory.CreateClient(CredentialsClient.HttpClientName),
                options,
                logger,
                sp.GetService<IA2AClient>());
        });
        return services;
    }

    /// <summary>
    /// Overload for hosts that want to configure options imperatively rather than from IConfiguration.
    /// </summary>
    public static IServiceCollection AddCredentialsClient(this IServiceCollection services, Action<CredentialsClientOptions> configure)
    {
        services.AddOptions<CredentialsClientOptions>().Configure(configure).ValidateOnStart();
        services.AddA2AClient();
        services.AddHttpClient(CredentialsClient.HttpClientName);
        services.AddSingleton<IWorkerAuthRevocationCache, InMemoryWorkerAuthRevocationCache>();
        services.AddSingleton<ICredentialsClient>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CredentialsClientOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CredentialsClient>>();
            return new CredentialsClient(
                httpFactory.CreateClient(CredentialsClient.HttpClientName),
                options,
                logger,
                sp.GetService<IA2AClient>());
        });
        return services;
    }

    /// <summary>
    /// Register the public local credential substitute that resolves
    /// <see cref="CredentialReference"/> values from process environment variables.
    /// </summary>
    public static IServiceCollection AddEnvironmentVariableCredentialResolver(this IServiceCollection services)
    {
        services.TryAddSingleton<ICredentialReferenceResolver, EnvironmentVariableCredentialReferenceResolver>();
        return services;
    }
}
