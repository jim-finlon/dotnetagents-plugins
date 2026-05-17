namespace DotNetAgents.ComputerUse;

/// <summary>Browser automation: navigate, selectors, fill, capture. FR-CU-003.</summary>
public interface IBrowserAgent
{
    /// <summary>Navigates to the URL and returns the page.</summary>
    Task<IBrowserPage> NavigateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Finds element by CSS selector.</summary>
    Task<IBrowserElement?> QuerySelectorAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>Finds element by natural language description (vision or heuristics).</summary>
    Task<IBrowserElement?> FindByDescriptionAsync(string naturalLanguageDescription, CancellationToken cancellationToken = default);

    /// <summary>Clicks the element.</summary>
    Task ClickAsync(IBrowserElement element, CancellationToken cancellationToken = default);

    /// <summary>Fills the element (e.g. input) with the value.</summary>
    Task FillAsync(IBrowserElement element, string value, CancellationToken cancellationToken = default);

    /// <summary>Captures a screenshot of the current page.</summary>
    Task<ScreenState> CaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the text content of the element.</summary>
    Task<string> GetTextAsync(IBrowserElement element, CancellationToken cancellationToken = default);

    /// <summary>Completes a task described in natural language (navigate, fill, submit, etc.).</summary>
    Task<WebTaskResult> CompleteTaskAsync(string taskDescription, CancellationToken cancellationToken = default);
}
