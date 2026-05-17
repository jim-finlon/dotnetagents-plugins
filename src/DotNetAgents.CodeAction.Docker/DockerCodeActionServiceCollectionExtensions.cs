using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.CodeAction.Docker;

/// <summary>DI registration helpers for the Docker sandbox runtime.</summary>
public static class DockerCodeActionServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="DockerSandboxRuntime"/> as the singleton <see cref="ICodeActionRuntime"/>.
    /// Caller MUST also register <see cref="CodeActionOptions"/> via <c>AddCodeAction()</c>.
    /// </summary>
    public static IServiceCollection AddDockerCodeActionRuntime(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<DockerSandboxOptions>(configuration.GetSection(DockerSandboxOptions.SectionName));
        }
        else
        {
            services.AddOptions<DockerSandboxOptions>();
        }

        services.TryAddSingleton<IDockerProcessRunner, DefaultDockerProcessRunner>();
        services.AddSingleton<ICodeActionRuntime, DockerSandboxRuntime>();
        return services;
    }
}
