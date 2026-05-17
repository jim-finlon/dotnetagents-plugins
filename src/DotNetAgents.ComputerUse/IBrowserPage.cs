namespace DotNetAgents.ComputerUse;

/// <summary>Browser page (tab) abstraction for browser agent. FR-CU-003.</summary>
public interface IBrowserPage
{
    /// <summary>Current URL.</summary>
    string Url { get; }

    /// <summary>Page title.</summary>
    string Title { get; }
}
