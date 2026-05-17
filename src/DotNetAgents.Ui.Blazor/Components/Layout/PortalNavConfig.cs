using Microsoft.Extensions.Configuration;

namespace DotNetAgents.Ui.Blazor.Components.Layout;

/// <summary>
/// Resolution rules for the host portal root URL referenced by downstream
/// Blazor UIs' <c>PortalNav</c> breadcrumb. Service-specific dashboard
/// registries live outside the shared UI package.
/// </summary>
public static class PortalNavConfig
{
    public const string DefaultPortalRootUrl = "/";
    public const string PortalRootUrlConfigKey = "Portal:RootUrl";

    /// <summary>
    /// Resolve the portal root URL using this precedence (first non-empty wins):
    /// 1. <paramref name="explicitOverride"/> (parameter passed to the component);
    /// 2. <c>Portal:RootUrl</c> from the host configuration;
    /// 3. <see cref="DefaultPortalRootUrl"/>.
    /// </summary>
    public static string Resolve(IConfiguration? configuration, string? explicitOverride)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return explicitOverride.Trim();

        var configured = configuration?[PortalRootUrlConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        return DefaultPortalRootUrl;
    }
}
