namespace DotNetAgents.ComputerUse;

/// <summary>Stub implementation that throws <see cref="NotSupportedException"/> for all operations. Use for testing or as a placeholder until an OS-specific implementation is registered.</summary>
public sealed class NoOpComputerAgent : IComputerAgent
{
    /// <inheritdoc />
    public Task<ScreenState> CaptureScreenAsync(DisplayOptions options, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task ClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task DoubleClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task RightClickAsync(ScreenCoordinates coords, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task KeyPressAsync(IReadOnlyList<KeyInput> keys, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task ScrollAsync(ScrollDirection direction, int amount, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task DragAsync(ScreenCoordinates from, ScreenCoordinates toCoordinates, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task<ElementInfo?> FindElementAsync(string description, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task InteractWithElementAsync(ElementInfo element, ElementInteractionType interactionType, string? value = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");

    /// <inheritdoc />
    public Task<IReadOnlyList<ElementInfo>> FindAllElementsAsync(string description, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No computer agent implementation is configured.");
}
