using Microsoft.Playwright;

namespace DotNetAgents.ComputerUse.Playwright;

/// <summary>Playwright-based implementation of <see cref="DotNetAgents.ComputerUse.IBrowserAgent"/>. CU-3.5.</summary>
public sealed class PlaywrightBrowserAgent : DotNetAgents.ComputerUse.IBrowserAgent, IAsyncDisposable
{
    private readonly PlaywrightBrowserAgentOptions _options;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    /// <summary>Creates a new Playwright browser agent.</summary>
    /// <param name="options">Optional configuration (headless, browser type, etc.).</param>
    public PlaywrightBrowserAgent(PlaywrightBrowserAgentOptions? options = null)
    {
        _options = options ?? new PlaywrightBrowserAgentOptions();
    }

    /// <inheritdoc />
    public async Task<DotNetAgents.ComputerUse.IBrowserPage> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = _options.NavigationTimeout }).ConfigureAwait(false);
        var title = await page.TitleAsync().ConfigureAwait(false);
        return new PlaywrightBrowserPage(page.Url, title ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<DotNetAgents.ComputerUse.IBrowserElement?> QuerySelectorAsync(string selector, CancellationToken cancellationToken = default)
    {
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = page.Locator(selector).First;
        var count = await locator.CountAsync().ConfigureAwait(false);
        if (count == 0)
            return null;
        var tagName = await locator.EvaluateAsync<string>("e => (e && e.tagName) ? e.tagName.toLowerCase() : ''").ConfigureAwait(false) ?? string.Empty;
        var text = await locator.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
        return new PlaywrightBrowserElement(selector, tagName, text);
    }

    /// <inheritdoc />
    public async Task<DotNetAgents.ComputerUse.IBrowserElement?> FindByDescriptionAsync(string naturalLanguageDescription, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(naturalLanguageDescription))
            return null;
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var desc = naturalLanguageDescription.Trim();

        // Heuristics: try role with name, then text, then label.
        var locator = page.GetByRole(AriaRole.Button, new() { Name = desc }).First;
        if (await locator.CountAsync().ConfigureAwait(false) > 0)
            return await ElementFromLocatorAsync(page, "playwright:role:button:name:" + desc, locator).ConfigureAwait(false);

        locator = page.GetByRole(AriaRole.Link, new() { Name = desc }).First;
        if (await locator.CountAsync().ConfigureAwait(false) > 0)
            return await ElementFromLocatorAsync(page, "playwright:role:link:name:" + desc, locator).ConfigureAwait(false);

        locator = page.GetByText(desc).First;
        if (await locator.CountAsync().ConfigureAwait(false) > 0)
            return await ElementFromLocatorAsync(page, "playwright:text:" + desc, locator).ConfigureAwait(false);

        locator = page.GetByLabel(desc).First;
        if (await locator.CountAsync().ConfigureAwait(false) > 0)
            return await ElementFromLocatorAsync(page, "playwright:label:" + desc, locator).ConfigureAwait(false);

        locator = page.GetByPlaceholder(desc).First;
        if (await locator.CountAsync().ConfigureAwait(false) > 0)
            return await ElementFromLocatorAsync(page, "playwright:placeholder:" + desc, locator).ConfigureAwait(false);

        return null;
    }

    /// <inheritdoc />
    public async Task ClickAsync(DotNetAgents.ComputerUse.IBrowserElement element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = ResolveLocator(page, element.Selector);
        await locator.ClickAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task FillAsync(DotNetAgents.ComputerUse.IBrowserElement element, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = ResolveLocator(page, element.Selector);
        await locator.FillAsync(value).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DotNetAgents.ComputerUse.ScreenState> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await page.ScreenshotAsync().ConfigureAwait(false);
        var viewport = page.ViewportSize;
        return new DotNetAgents.ComputerUse.ScreenState
        {
            ImageBytes = bytes ?? Array.Empty<byte>(),
            ContentType = "image/png",
            Width = viewport?.Width ?? 0,
            Height = viewport?.Height ?? 0
        };
    }

    /// <inheritdoc />
    public async Task<string> GetTextAsync(DotNetAgents.ComputerUse.IBrowserElement element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var page = await GetOrCreatePageAsync(cancellationToken).ConfigureAwait(false);
        var locator = ResolveLocator(page, element.Selector);
        return await locator.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
    }

    /// <inheritdoc />
    public Task<DotNetAgents.ComputerUse.WebTaskResult> CompleteTaskAsync(string taskDescription, CancellationToken cancellationToken = default)
    {
        // Natural-language task completion would require LLM integration; not implemented in this provider.
        return Task.FromResult(new DotNetAgents.ComputerUse.WebTaskResult
        {
            Success = false,
            Summary = string.Empty,
            Error = "Natural language task completion (CompleteTaskAsync) requires LLM integration; not implemented in Playwright provider."
        });
    }

    /// <summary>Disposes the browser and Playwright instance.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_page != null) { await _page.CloseAsync().ConfigureAwait(false); _page = null; }
        if (_context != null) { await _context.CloseAsync().ConfigureAwait(false); _context = null; }
        if (_browser != null) { await _browser.CloseAsync().ConfigureAwait(false); _browser = null; }
        _playwright?.Dispose();
        _playwright = null;
    }

    private async Task<IPage> GetOrCreatePageAsync(CancellationToken cancellationToken)
    {
        if (_page != null)
            return _page;
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        var browserType = _options.BrowserType switch
        {
            PlaywrightBrowserType.Chromium => _playwright.Chromium,
            PlaywrightBrowserType.Firefox => _playwright.Firefox,
            PlaywrightBrowserType.WebKit => _playwright.Webkit,
            _ => _playwright.Chromium
        };
        _browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _options.Headless }).ConfigureAwait(false);
        _context = await _browser.NewContextAsync().ConfigureAwait(false);
        _page = await _context.NewPageAsync().ConfigureAwait(false);
        return _page;
    }

    private static async Task<DotNetAgents.ComputerUse.IBrowserElement> ElementFromLocatorAsync(IPage page, string selectorKey, ILocator locator)
    {
        var tagName = await locator.EvaluateAsync<string>("e => (e && e.tagName) ? e.tagName.toLowerCase() : ''").ConfigureAwait(false) ?? string.Empty;
        var text = await locator.InnerTextAsync().ConfigureAwait(false) ?? string.Empty;
        return new PlaywrightBrowserElement(selectorKey, tagName, text);
    }

    private static ILocator ResolveLocator(IPage page, string selector)
    {
        if (selector.StartsWith("playwright:role:button:name:", StringComparison.Ordinal))
        {
            var name = selector.Substring("playwright:role:button:name:".Length);
            return page.GetByRole(AriaRole.Button, new() { Name = name }).First;
        }
        if (selector.StartsWith("playwright:role:link:name:", StringComparison.Ordinal))
        {
            var name = selector.Substring("playwright:role:link:name:".Length);
            return page.GetByRole(AriaRole.Link, new() { Name = name }).First;
        }
        if (selector.StartsWith("playwright:text:", StringComparison.Ordinal))
        {
            var text = selector.Substring("playwright:text:".Length);
            return page.GetByText(text).First;
        }
        if (selector.StartsWith("playwright:label:", StringComparison.Ordinal))
        {
            var label = selector.Substring("playwright:label:".Length);
            return page.GetByLabel(label).First;
        }
        if (selector.StartsWith("playwright:placeholder:", StringComparison.Ordinal))
        {
            var placeholder = selector.Substring("playwright:placeholder:".Length);
            return page.GetByPlaceholder(placeholder).First;
        }
        return page.Locator(selector).First;
    }
}
