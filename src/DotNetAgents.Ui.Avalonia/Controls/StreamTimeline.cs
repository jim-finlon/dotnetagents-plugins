using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed class StreamTimeline : ItemsControl
{
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        if (item is not StreamEventModel evt)
        {
            return base.CreateContainerForItemOverride(item, index, recycleKey);
        }

        var palette = DnaAvaloniaPalette.Dark;
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = evt.Title, Foreground = palette.TextPrimary, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"{evt.Source} - {evt.Timestamp.LocalDateTime:g}", Foreground = palette.TextMuted, FontSize = 12 },
                new TextBlock { Text = evt.Summary, Foreground = palette.TextSecondary, TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}
