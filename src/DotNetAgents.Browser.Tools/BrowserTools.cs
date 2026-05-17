using System.Text.Json;
using DotNetAgents.Abstractions.Tools;
using DotNetAgents.ComputerUse;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Browser.Tools;

public interface IBrowserToolPolicy
{
    Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default);
}

public interface IBrowserToolAuditSink
{
    Task WriteAsync(string toolName, string url, bool allowed, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default);
}

public sealed class AllowAllBrowserToolPolicy : IBrowserToolPolicy
{
    public Task<bool> IsAllowedAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public sealed class NullBrowserToolAuditSink : IBrowserToolAuditSink
{
    public Task WriteAsync(string toolName, string url, bool allowed, DateTimeOffset timestampUtc, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class BrowserNavigateTool(
    IBrowserAgent browserAgent,
    IBrowserToolPolicy policy,
    IBrowserToolAuditSink auditSink) : ITool
{
    private readonly IBrowserAgent _browserAgent = browserAgent;
    private readonly IBrowserToolPolicy _policy = policy;
    private readonly IBrowserToolAuditSink _auditSink = auditSink;

    public string Name => "browser_navigate";
    public string Description => "Navigates browser to a URL and captures screen metadata.";

    public JsonElement InputSchema => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            url = new { type = "string", description = "Absolute URL to navigate to." }
        },
        required = new[] { "url" }
    });

    public async Task<ToolResult> ExecuteAsync(object input, CancellationToken cancellationToken = default)
    {
        if (input is not JsonElement json || !json.TryGetProperty("url", out var urlElement))
        {
            return ToolResult.Failure("Input must include 'url'.");
        }

        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return ToolResult.Failure("Input 'url' cannot be empty.");
        }

        var allowed = await _policy.IsAllowedAsync(url, cancellationToken).ConfigureAwait(false);
        await _auditSink.WriteAsync(Name, url, allowed, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            return ToolResult.Failure("Browser navigation blocked by policy.");
        }

        await _browserAgent.NavigateAsync(url, cancellationToken).ConfigureAwait(false);
        var state = await _browserAgent.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return ToolResult.Success(new { url, state.Width, state.Height, state.ContentType });
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDotNetAgentsBrowserTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IBrowserToolPolicy, AllowAllBrowserToolPolicy>();
        services.AddSingleton<IBrowserToolAuditSink, NullBrowserToolAuditSink>();
        services.AddScoped<ITool, BrowserNavigateTool>();
        return services;
    }
}
