// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// Transparent overlay that hosts one <see cref="CanvasMenu"/> and swallows the dismiss click.
/// The diagram and site-plan canvases position their own menus because they need the node
/// geometry to do it; the list-shaped views have no such constraint, so they drop one of these
/// over themselves and let it own both placement and dismissal.
/// </summary>
public class ContextMenuLayer : Canvas
{
    public ContextMenuLayer()
    {
        IsVisible = false;
        Background = Brushes.Transparent;
        PointerPressed += (_, e) =>
        {
            if (e.Source != this) return;
            Close();
            e.Handled = true;
        };
    }

    public void Show(string header, IReadOnlyList<(string Label, Action Action)> items, Point point)
    {
        if (items.Count == 0) return;
        Children.Clear();

        var menu = CanvasMenu.Build(header, items, Close);
        var estimatedHeight = CanvasMenu.EstimateHeight(items.Count);
        SetLeft(menu, Math.Max(0, Math.Min(point.X, Bounds.Width - CanvasMenu.Width - 10)));
        SetTop(menu, Math.Max(0, Math.Min(point.Y, Bounds.Height - estimatedHeight)));
        Children.Add(menu);
        IsVisible = true;
    }

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Children.Clear();
    }
}
