using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.CodeAction;

/// <summary>
/// DI registration helpers for the code-action runtime abstractions.
/// </summary>
public static class CodeActionServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="CodeActionOptions"/> and the <see cref="CodeActionAgent"/> orchestrator.
    /// Callers MUST also register an <see cref="ICodeActionRuntime"/> implementation
    /// (Docker, Pyodide, or — testing only — <see cref="UnsandboxedTestRuntime"/>).
    /// </summary>
    public static IServiceCollection AddCodeAction(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<CodeActionOptions>(configuration.GetSection(CodeActionOptions.SectionName));
        }
        else
        {
            services.AddOptions<CodeActionOptions>();
        }

        services.TryAddSingleton<IExecutionSandboxManager, DefaultExecutionSandboxManager>();
        services.TryAddSingleton<CodeActionAgent>();
        return services;
    }
}
