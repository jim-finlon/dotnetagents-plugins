namespace DotNetAgents.ComputerUse;

/// <summary>Browser DOM element abstraction. FR-CU-003.</summary>
public interface IBrowserElement
{
    /// <summary>CSS selector that identifies this element.</summary>
    string Selector { get; }

    /// <summary>Tag name (e.g. "button", "input").</summary>
    string TagName { get; }

    /// <summary>Visible text content.</summary>
    string Text { get; }
}
