using Avalonia;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia;

public sealed record DnaAvaloniaPalette(
    IBrush Page,
    IBrush Raised,
    IBrush Card,
    IBrush Border,
    IBrush TextPrimary,
    IBrush TextSecondary,
    IBrush TextMuted,
    IBrush Primary,
    IBrush Info,
    IBrush Success,
    IBrush Warning,
    IBrush Danger)
{
    public static DnaAvaloniaPalette Dark { get; } = new(
        Brush.Parse("#0f172a"),
        Brush.Parse("#111827"),
        Brush.Parse("#1f2937"),
        Brush.Parse("#334155"),
        Brush.Parse("#f8fafc"),
        Brush.Parse("#cbd5e1"),
        Brush.Parse("#94a3b8"),
        Brush.Parse("#3b82f6"),
        Brush.Parse("#06b6d4"),
        Brush.Parse("#22c55e"),
        Brush.Parse("#f59e0b"),
        Brush.Parse("#ef4444"));

    public IBrush IntentBrush(UiIntent intent) => intent switch
    {
        UiIntent.Primary => Primary,
        UiIntent.Info => Info,
        UiIntent.Success => Success,
        UiIntent.Warning => Warning,
        UiIntent.Danger => Danger,
        _ => TextSecondary
    };
}

public static class DnaAvaloniaMetrics
{
    public static double Spacing(UiDensity density) => density switch
    {
        UiDensity.Compact => 8,
        UiDensity.Spacious => 18,
        _ => 12
    };

    public static Thickness CardPadding(UiDensity density)
    {
        var size = density switch
        {
            UiDensity.Compact => 12,
            UiDensity.Spacious => 24,
            _ => 16
        };

        return new Thickness(size);
    }
}
