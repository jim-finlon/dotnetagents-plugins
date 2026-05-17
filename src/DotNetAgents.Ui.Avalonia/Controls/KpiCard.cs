using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed class KpiCard : Border
{
    public static readonly StyledProperty<KpiWidgetModel?> ModelProperty =
        AvaloniaProperty.Register<KpiCard, KpiWidgetModel?>(nameof(Model));

    public static readonly StyledProperty<UiDensity> DensityProperty =
        AvaloniaProperty.Register<KpiCard, UiDensity>(nameof(Density), UiDensity.Comfortable);

    public KpiWidgetModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public UiDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ModelProperty || change.Property == DensityProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        var palette = DnaAvaloniaPalette.Dark;
        Background = palette.Card;
        BorderBrush = palette.Border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = DnaAvaloniaMetrics.CardPadding(Density);

        if (Model is null)
        {
            Child = null;
            return;
        }

        var delta = Model.DeltaPercent is null
            ? null
            : new StatusBadge
            {
                Text = $"{Model.DeltaPercent:+0.0;-0.0;0.0}%",
                Intent = Model.Intent
            };

        var body = new StackPanel
        {
            Spacing = DnaAvaloniaMetrics.Spacing(Density),
            Children =
            {
                new TextBlock { Text = Model.Label, Foreground = palette.TextMuted, FontSize = 12 },
                new TextBlock { Text = Model.Value, Foreground = palette.TextPrimary, FontSize = 28, FontWeight = FontWeight.Bold }
            }
        };

        if (!string.IsNullOrWhiteSpace(Model.SubLabel))
        {
            body.Children.Add(new TextBlock { Text = Model.SubLabel, Foreground = palette.TextSecondary, FontSize = 13 });
        }

        if (delta is not null)
        {
            body.Children.Add(delta);
        }

        Child = body;
    }
}
