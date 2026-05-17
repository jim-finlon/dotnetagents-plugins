namespace DotNetAgents.ComputerUse;

/// <summary>Screenshot-based computer control: capture and input injection. FR-CU-001, FR-CU-002.</summary>
public interface IComputerAgent
{
    /// <summary>Captures the screen (or region) per options.</summary>
    Task<ScreenState> CaptureScreenAsync(DisplayOptions options, CancellationToken cancellationToken = default);

    /// <summary>Left click at coordinates.</summary>
    Task ClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default);

    /// <summary>Double left click at coordinates.</summary>
    Task DoubleClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default);

    /// <summary>Right click at coordinates.</summary>
    Task RightClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default);

    /// <summary>Types the given text (keyboard input).</summary>
    Task TypeAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Sends key press(es) (e.g. modifier + key).</summary>
    Task KeyPressAsync(IReadOnlyList<KeyInput> keys, CancellationToken cancellationToken = default);

    /// <summary>Scrolls in the given direction by amount.</summary>
    Task ScrollAsync(ScrollDirection direction, int amount, CancellationToken cancellationToken = default);

    /// <summary>Drags from one point to another.</summary>
    Task DragAsync(ScreenCoordinates from, ScreenCoordinates toCoordinates, CancellationToken cancellationToken = default);

    /// <summary>Finds an element matching the description (e.g. via vision). FR-CU-002.</summary>
    Task<ElementInfo?> FindElementAsync(string description, CancellationToken cancellationToken = default);

    /// <summary>Performs interaction on the element (click, type, etc.).</summary>
    Task InteractWithElementAsync(ElementInfo element, ElementInteractionType interactionType, string? value = null, CancellationToken cancellationToken = default);

    /// <summary>Finds all elements matching the description.</summary>
    Task<IReadOnlyList<ElementInfo>> FindAllElementsAsync(string description, CancellationToken cancellationToken = default);
}
