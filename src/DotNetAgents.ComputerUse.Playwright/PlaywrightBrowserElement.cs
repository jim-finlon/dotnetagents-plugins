namespace DotNetAgents.ComputerUse.Playwright;

/// <summary>Wraps a Playwright locator as <see cref="DotNetAgents.ComputerUse.IBrowserElement"/> with cached selector, tag name, and text.</summary>
public sealed class PlaywrightBrowserElement : DotNetAgents.ComputerUse.IBrowserElement
{
    /// <inheritdoc />
    public string Selector { get; }

    /// <inheritdoc />
    public string TagName { get; }

    /// <inheritdoc />
    public string Text { get; }

    /// <summary>Creates a browser element wrapper with the given selector, tag name, and text.</summary>
    public PlaywrightBrowserElement(string selector, string tagName, string text)
    {
        Selector = selector;
        TagName = tagName;
        Text = text;
    }
}
