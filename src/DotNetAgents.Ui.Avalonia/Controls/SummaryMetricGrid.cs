using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed class SummaryMetricGrid : ItemsControl
{
    public static readonly StyledProperty<UiDensity> DensityProperty =
        AvaloniaProperty.Register<SummaryMetricGrid, UiDensity>(nameof(Density), UiDensity.Comfortable);

    public UiDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        if (item is not SummaryMetricModel metric)
        {
            return base.CreateContainerForItemOverride(item, index, recycleKey);
        }

        var palette = DnaAvaloniaPalette.Dark;
        return new Border
        {
            Background = palette.Raised,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = DnaAvaloniaMetrics.CardPadding(Density),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = metric.Label, Foreground = palette.TextMuted, FontSize = 12 },
                    new TextBlock { Text = metric.Value, Foreground = palette.IntentBrush(metric.Intent), FontSize = 22, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = metric.Note ?? string.Empty, Foreground = palette.TextSecondary, FontSize = 12 }
                }
            }
        };
    }
}
