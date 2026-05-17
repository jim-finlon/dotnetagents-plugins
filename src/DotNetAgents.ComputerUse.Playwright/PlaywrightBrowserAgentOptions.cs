namespace DotNetAgents.ComputerUse.Playwright;

/// <summary>Configuration for <see cref="PlaywrightBrowserAgent"/>.</summary>
public sealed class PlaywrightBrowserAgentOptions
{
    /// <summary>Run browser headless. Default is true.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>Browser type to launch. Default is Chromium.</summary>
    public PlaywrightBrowserType BrowserType { get; init; } = PlaywrightBrowserType.Chromium;

    /// <summary>Navigation timeout in milliseconds. Default is 30000.</summary>
    public float NavigationTimeout { get; init; } = 30_000;
}

/// <summary>Browser engine for Playwright.</summary>
public enum PlaywrightBrowserType
{
    /// <summary>Chromium.</summary>
    Chromium,

    /// <summary>Firefox.</summary>
    Firefox,

    /// <summary>WebKit.</summary>
    WebKit
}
