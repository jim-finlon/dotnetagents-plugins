using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace DotNetAgents.ComputerUse.Windows;

/// <summary>Windows implementation of <see cref="DotNetAgents.ComputerUse.IComputerAgent"/>: GDI screen capture and SendInput for mouse/keyboard. CU-3.2. Optional <see cref="DotNetAgents.ComputerUse.IVisionElementDetector"/> for CU-3.3.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsComputerAgent : DotNetAgents.ComputerUse.IComputerAgent
{
    private readonly DotNetAgents.ComputerUse.IVisionElementDetector? _visionDetector;

    /// <summary>Creates an agent with screen capture and input only (FindElement/Interact/FindAll throw).</summary>
    public WindowsComputerAgent() => _visionDetector = null;

    /// <summary>Creates an agent that uses the given vision detector for FindElementAsync, FindAllElementsAsync, and InteractWithElementAsync.</summary>
    public WindowsComputerAgent(DotNetAgents.ComputerUse.IVisionElementDetector visionDetector)
    {
        _visionDetector = visionDetector ?? throw new ArgumentNullException(nameof(visionDetector));
    }

    /// <inheritdoc />
    public Task<DotNetAgents.ComputerUse.ScreenState> CaptureScreenAsync(DotNetAgents.ComputerUse.DisplayOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsComputerAgent is supported only on Windows.");

        cancellationToken.ThrowIfCancellationRequested();
        int x = 0, y = 0, width, height;
        if (options.FullScreen)
        {
            width = WindowsInputNative.GetSystemMetrics(0); // SM_CXSCREEN
            height = WindowsInputNative.GetSystemMetrics(1); // SM_CYSCREEN
        }
        else if (options.Region is { } r)
        {
            x = r.X;
            y = r.Y;
            width = r.Width;
            height = r.Height;
        }
        else
        {
            width = WindowsInputNative.GetSystemMetrics(0);
            height = WindowsInputNative.GetSystemMetrics(1);
        }

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var state = new DotNetAgents.ComputerUse.ScreenState
        {
            ImageBytes = ms.ToArray(),
            ContentType = "image/png",
            Width = width,
            Height = height
        };
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task ClickAsync(DotNetAgents.ComputerUse.ScreenCoordinates coords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coords);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        WindowsInputNative.MouseClick(coords.X, coords.Y, rightButton: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DoubleClickAsync(DotNetAgents.ComputerUse.ScreenCoordinates coords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coords);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        WindowsInputNative.MouseDoubleClick(coords.X, coords.Y);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RightClickAsync(DotNetAgents.ComputerUse.ScreenCoordinates coords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coords);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        WindowsInputNative.MouseClick(coords.X, coords.Y, rightButton: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        foreach (char c in text)
        {
            short vk = WindowsInputNative.VkKeyScan(c);
            if (vk == -1)
                continue;
            ushort keyCode = (ushort)(vk & 0xFF);
            bool shift = (vk & 0x100) != 0;
            if (shift)
                WindowsInputNative.SendKeyDown(0x10); // VK_SHIFT
            WindowsInputNative.SendKeyDown(keyCode);
            WindowsInputNative.SendKeyUp(keyCode);
            if (shift)
                WindowsInputNative.SendKeyUp(0x10);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task KeyPressAsync(IReadOnlyList<DotNetAgents.ComputerUse.KeyInput> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var keyInput in keys)
        {
            foreach (var mod in keyInput.Modifiers ?? [])
            {
                ushort modVk = VirtualKeyFromName(mod);
                if (modVk != 0)
                    WindowsInputNative.SendKeyDown(modVk);
            }
            ushort vk = VirtualKeyFromName(keyInput.Key);
            if (vk != 0)
            {
                WindowsInputNative.SendKeyDown(vk);
                WindowsInputNative.SendKeyUp(vk);
            }
            for (int i = (keyInput.Modifiers?.Count ?? 0) - 1; i >= 0; i--)
            {
                ushort modVk = VirtualKeyFromName(keyInput.Modifiers![i]);
                if (modVk != 0)
                    WindowsInputNative.SendKeyUp(modVk);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ScrollAsync(DotNetAgents.ComputerUse.ScrollDirection direction, int amount, CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        int x = WindowsInputNative.GetSystemMetrics(0) / 2;
        int y = WindowsInputNative.GetSystemMetrics(1) / 2;
        int delta = direction switch
        {
            DotNetAgents.ComputerUse.ScrollDirection.Up => amount,
            DotNetAgents.ComputerUse.ScrollDirection.Down => -amount,
            DotNetAgents.ComputerUse.ScrollDirection.Left => amount,
            DotNetAgents.ComputerUse.ScrollDirection.Right => -amount,
            _ => amount
        };
        bool vertical = direction is DotNetAgents.ComputerUse.ScrollDirection.Up or DotNetAgents.ComputerUse.ScrollDirection.Down;
        WindowsInputNative.MouseScroll(x, y, vertical, delta);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DragAsync(DotNetAgents.ComputerUse.ScreenCoordinates from, DotNetAgents.ComputerUse.ScreenCoordinates toCoordinates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(toCoordinates);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        WindowsInputNative.MouseDrag(from.X, from.Y, toCoordinates.X, toCoordinates.Y);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<DotNetAgents.ComputerUse.ElementInfo?> FindElementAsync(string description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (_visionDetector == null)
            throw new NotSupportedException("FindElementAsync requires a vision detector; pass IVisionElementDetector to the constructor.");
        var screen = await CaptureScreenAsync(new DotNetAgents.ComputerUse.DisplayOptions { FullScreen = true }, cancellationToken).ConfigureAwait(false);
        var elements = await _visionDetector.DetectElementsAsync(screen, description, cancellationToken).ConfigureAwait(false);
        var desc = description.Trim();
        return elements.FirstOrDefault(e => e.Description.Contains(desc, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task InteractWithElementAsync(DotNetAgents.ComputerUse.ElementInfo element, DotNetAgents.ComputerUse.ElementInteractionType interactionType, string? value = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var center = element.Center;
        switch (interactionType)
        {
            case DotNetAgents.ComputerUse.ElementInteractionType.Click:
                await ClickAsync(center, cancellationToken).ConfigureAwait(false);
                break;
            case DotNetAgents.ComputerUse.ElementInteractionType.DoubleClick:
                await DoubleClickAsync(center, cancellationToken).ConfigureAwait(false);
                break;
            case DotNetAgents.ComputerUse.ElementInteractionType.RightClick:
                await RightClickAsync(center, cancellationToken).ConfigureAwait(false);
                break;
            case DotNetAgents.ComputerUse.ElementInteractionType.Type:
                if (!string.IsNullOrEmpty(value))
                    await TypeAsync(value, cancellationToken).ConfigureAwait(false);
                break;
            case DotNetAgents.ComputerUse.ElementInteractionType.ScrollIntoView:
                await ScrollAsync(DotNetAgents.ComputerUse.ScrollDirection.Up, 1, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await ClickAsync(center, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DotNetAgents.ComputerUse.ElementInfo>> FindAllElementsAsync(string description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (_visionDetector == null)
            throw new NotSupportedException("FindAllElementsAsync requires a vision detector; pass IVisionElementDetector to the constructor.");
        var screen = await CaptureScreenAsync(new DotNetAgents.ComputerUse.DisplayOptions { FullScreen = true }, cancellationToken).ConfigureAwait(false);
        var elements = await _visionDetector.DetectElementsAsync(screen, description, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(description))
            return elements;
        var desc = description.Trim();
        return elements.Where(e => e.Description.Contains(desc, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WindowsComputerAgent is supported only on Windows.");
    }

    private static ushort VirtualKeyFromName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return 0;
        return name.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" => 0x20,
            "CONTROL" or "CTRL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "WIN" or "WINDOWS" => 0x5B,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0
        };
    }
}
