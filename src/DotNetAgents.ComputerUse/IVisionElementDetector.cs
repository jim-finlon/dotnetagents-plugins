namespace DotNetAgents.ComputerUse;

/// <summary>Detects UI elements on a screen capture using vision (e.g. IMultiModalModel). FR-CU-002, CU-3.3.</summary>
public interface IVisionElementDetector
{
    /// <summary>Detects elements in the screenshot. When <paramref name="descriptionHint"/> is provided, may return only matching elements.</summary>
    /// <param name="screen">Captured screen state (image and dimensions).</param>
    /// <param name="descriptionHint">Optional filter (e.g. "button", "submit") to limit or rank results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detected elements with bounds and descriptions; may be empty if vision fails or finds nothing.</returns>
    Task<IReadOnlyList<ElementInfo>> DetectElementsAsync(ScreenState screen, string? descriptionHint, CancellationToken cancellationToken = default);
}
