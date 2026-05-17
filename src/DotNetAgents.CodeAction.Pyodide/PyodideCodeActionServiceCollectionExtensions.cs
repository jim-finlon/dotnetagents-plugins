using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.CodeAction.Pyodide;

/// <summary>DI registration helpers for the Pyodide sandbox runtime.</summary>
public static class PyodideCodeActionServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="PyodideSandboxRuntime"/> as the singleton <see cref="ICodeActionRuntime"/>.
    /// Caller MUST also register <see cref="CodeActionOptions"/> via <c>AddCodeAction()</c> AND
    /// register an <see cref="IPyodideHost"/> implementation (default ProcessPyodideHost lives
    /// in the host application — see docs/runbooks/code-action-pyodide-install.md).
    /// </summary>
    public static IServiceCollection AddPyodideCodeActionRuntime(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<PyodideSandboxOptions>(configuration.GetSection(PyodideSandboxOptions.SectionName));
        }
        else
        {
            services.AddOptions<PyodideSandboxOptions>();
        }

        services.AddSingleton<ICodeActionRuntime, PyodideSandboxRuntime>();
        return services;
    }
}
