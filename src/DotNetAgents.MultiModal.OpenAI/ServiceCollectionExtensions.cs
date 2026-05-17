using DotNetAgents.Ecosystem;
using DotNetAgents.MultiModal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.MultiModal.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI multi-modal services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenAI multi-modal services using the canonical DotNetAgents plugin registration convention.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Options callback. Values should come from configuration or CredentialsAgent references, not embedded secrets.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDotNetAgentsOpenAIMultiModal(
        this IServiceCollection services,
        Action<OpenAIMultiModalOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new OpenAIMultiModalOptions();
        configureOptions(options);
        options.Validate();

        services.AddDotNetAgentsEcosystem();
        services.AddPlugin(new OpenAIMultiModalPlugin());

        services.AddHttpClient<OpenAIMultiModalModel>(client =>
        {
            options.ConfigureHttpClient?.Invoke(client);
        });

        services.AddSingleton<IMultiModalModel>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAIMultiModalModel));
            var logger = sp.GetService<ILogger<OpenAIMultiModalModel>>();
            return new OpenAIMultiModalModel(httpClient, options.ApiKey, options.ModelName, options.VisionModel, logger);
        });

        return services;
    }
}
