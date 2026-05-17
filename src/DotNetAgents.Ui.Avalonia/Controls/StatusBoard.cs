using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed class StatusBoard : ItemsControl
{
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        if (item is not BoardColumnModel column)
        {
            return base.CreateContainerForItemOverride(item, index, recycleKey);
        }

        var palette = DnaAvaloniaPalette.Dark;
        var cards = new StackPanel { Spacing = 8 };
        foreach (var card in column.Cards)
        {
            cards.Children.Add(new Border
            {
                Background = palette.Card,
                BorderBrush = palette.IntentBrush(card.Intent),
                BorderThickness = new Thickness(1, 0, 0, 0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = card.Title, Foreground = palette.TextPrimary, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = card.Meta ?? card.SecondaryText ?? string.Empty, Foreground = palette.TextMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap }
                    }
                }
            });
        }

        return new Border
        {
            Background = palette.Raised,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MinWidth = 240,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = column.Label, Foreground = palette.TextPrimary, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = column.Summary ?? string.Empty, Foreground = palette.TextSecondary, FontSize = 12 },
                    cards
                }
            }
        };
    }
}
