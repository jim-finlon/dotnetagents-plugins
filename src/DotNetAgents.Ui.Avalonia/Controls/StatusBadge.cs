using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed class StatusBadge : Border
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<UiIntent> IntentProperty =
        AvaloniaProperty.Register<StatusBadge, UiIntent>(nameof(Intent), UiIntent.Neutral);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public UiIntent Intent
    {
        get => GetValue(IntentProperty);
        set => SetValue(IntentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == IntentProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        var palette = DnaAvaloniaPalette.Dark;
        var brush = palette.IntentBrush(Intent);
        Background = new SolidColorBrush(((ISolidColorBrush)brush).Color, 0.15);
        BorderBrush = brush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(999);
        Padding = new Thickness(8, 3);
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = new TextBlock
        {
            Text = Text,
            Foreground = brush,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
    }
}
