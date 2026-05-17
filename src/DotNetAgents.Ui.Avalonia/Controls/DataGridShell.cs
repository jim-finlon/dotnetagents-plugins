using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using DotNetAgents.Ui.Core;

namespace DotNetAgents.Ui.Avalonia.Controls;

public sealed record DataGridShellModel(
    IReadOnlyList<GridColumnDefinition> Columns,
    IReadOnlyList<GridRowModel> Rows);

public sealed class DataGridShell : ItemsControl
{
    public static readonly StyledProperty<IReadOnlyList<GridColumnDefinition>> ColumnsProperty =
        AvaloniaProperty.Register<DataGridShell, IReadOnlyList<GridColumnDefinition>>(nameof(Columns), Array.Empty<GridColumnDefinition>());

    public IReadOnlyList<GridColumnDefinition> Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        if (item is not GridRowModel row)
        {
            return base.CreateContainerForItemOverride(item, index, recycleKey);
        }

        var palette = DnaAvaloniaPalette.Dark;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var column in Columns)
        {
            row.Cells.TryGetValue(column.Key, out var value);
            panel.Children.Add(new TextBlock
            {
                Text = value ?? string.Empty,
                Foreground = column.Numeric ? palette.IntentBrush(row.Intent) : palette.TextSecondary,
                Width = ParseWidth(column.Width),
                TextAlignment = column.Numeric ? TextAlignment.Right : TextAlignment.Left
            });
        }

        return new Border
        {
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8),
            Child = panel
        };
    }

    private static double ParseWidth(string? width)
    {
        if (string.IsNullOrWhiteSpace(width))
        {
            return 140;
        }

        var digits = new string(width.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, out var parsed) ? parsed : 140;
    }
}
