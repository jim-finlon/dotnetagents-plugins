namespace DotNetAgents.ComputerUse.Playwright;

/// <summary>Wraps a Playwright page as <see cref="DotNetAgents.ComputerUse.IBrowserPage"/>.</summary>
public sealed class PlaywrightBrowserPage : DotNetAgents.ComputerUse.IBrowserPage
{
    /// <inheritdoc />
    public string Url { get; }

    /// <inheritdoc />
    public string Title { get; }

    /// <summary>Creates a browser page wrapper with the given URL and title.</summary>
    public PlaywrightBrowserPage(string url, string title)
    {
        Url = url;
        Title = title;
    }
}
