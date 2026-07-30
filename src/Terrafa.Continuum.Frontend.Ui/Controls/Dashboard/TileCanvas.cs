// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Dashboard;

/// <summary>A tile on the canvas, together with the chrome the canvas wraps it in.</summary>
public sealed class TilePlacement
{
    internal TilePlacement(DashboardTile tile, Border container, ContentControl host)
    {
        Tile = tile;
        Container = container;
        Host = host;
    }

    public DashboardTile Tile { get; }
    internal Border Container { get; }
    internal ContentControl Host { get; }

    public Point Position => new(Canvas.GetLeft(Container), Canvas.GetTop(Container));

    public Size Size => new(Container.Width, Container.Height);

    /// <summary>Swaps the rendered body without disturbing position, size or selection.</summary>
    public void SetContent(Control content) => Host.Content = content;
}

/// <summary>
/// The dashboard's free canvas: tiles sit wherever they are dropped, drag with the pointer, resize
/// from a corner grip, and the background pans. Deliberately not the network's
/// <see cref="Diagram.DiagramCanvas"/> — that one exists to draw ports and edges between nodes, and
/// a dashboard tile has neither.
/// </summary>
public class TileCanvas : Border
{
    private const double MinTileWidth = 220;
    private const double MinTileHeight = 150;
    private const double GripSize = 14;

    private readonly Canvas world;
    private readonly Canvas menuLayer;
    private readonly TranslateTransform pan = new();
    private readonly List<TilePlacement> placements = [];

    private TilePlacement? draggingTile;
    private Point dragPointerStart;
    private Point dragTileStart;

    private TilePlacement? resizingTile;
    private Point resizePointerStart;
    private Size resizeStart;

    private bool panning;
    private Point panPointerStart;
    private Point panStart;
    private int topZ;

    public Func<DashboardTile, IReadOnlyList<(string Label, Action Action)>>? MenuProvider { get; set; }

    /// <summary>Raised on double-click — the gesture that opens a tile's editor.</summary>
    public event Action<DashboardTile>? TileActivated;

    public event Action<DashboardTile>? TileMoved;

    public IReadOnlyList<TilePlacement> Placements => placements;

    public TileCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;

        world = new Canvas { RenderTransform = pan };
        menuLayer = new Canvas { IsVisible = false, Background = Brushes.Transparent };
        menuLayer.PointerPressed += OnMenuLayerPressed;

        Child = new Panel { Children = { world, menuLayer } };

        PointerPressed += OnBackgroundPressed;
        PointerMoved += OnBackgroundMoved;
        PointerReleased += OnBackgroundReleased;
    }

    public TilePlacement AddTile(DashboardTile tile, Control content, Point position, Size size)
    {
        var host = new ContentControl { Content = content };
        var container = new Border
        {
            Width = Math.Max(size.Width, MinTileWidth),
            Height = Math.Max(size.Height, MinTileHeight),
            Background = Palette.BgPanel,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            ClipToBounds = true
        };

        var grip = BuildGrip();
        container.Child = new Panel { Children = { host, grip } };

        var placement = new TilePlacement(tile, container, host);

        Canvas.SetLeft(container, position.X);
        Canvas.SetTop(container, position.Y);
        container.ZIndex = ++topZ;

        container.PointerPressed += (_, e) => OnTilePressed(placement, e);
        container.PointerMoved += (_, e) => OnTileMoved(placement, e);
        container.PointerReleased += (_, e) => OnTileReleased(placement, e);

        grip.PointerPressed += (_, e) => OnGripPressed(placement, e);
        grip.PointerMoved += (_, e) => OnGripMoved(placement, e);
        grip.PointerReleased += (_, e) => OnGripReleased(placement, e);

        world.Children.Add(container);
        placements.Add(placement);
        return placement;
    }

    public void RemoveTile(DashboardTile tile)
    {
        if (Find(tile) is not { } placement) return;
        world.Children.Remove(placement.Container);
        placements.Remove(placement);
    }

    /// <summary>Empties the canvas without disturbing the pan, so a redraw stays where the operator left it.</summary>
    public void Clear()
    {
        foreach (var placement in placements) world.Children.Remove(placement.Container);
        placements.Clear();
    }

    public TilePlacement? Find(DashboardTile tile) =>
        placements.FirstOrDefault(placement => placement.Tile == tile);

    public Point ViewportToWorld(Point viewportPoint) => new(viewportPoint.X - pan.X, viewportPoint.Y - pan.Y);

    /// <summary>Marks one tile as the edited one, so the canvas shows what the right-hand pane is on.</summary>
    public void SetSelected(DashboardTile? tile)
    {
        foreach (var placement in placements)
        {
            var selected = placement.Tile == tile;
            placement.Container.BorderBrush = selected ? Palette.Amber : Palette.Border;
            placement.Container.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private static Border BuildGrip() => new()
    {
        Width = GripSize,
        Height = GripSize,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Background = Brushes.Transparent,
        BorderBrush = Palette.BorderMid,
        BorderThickness = new Thickness(1, 1, 0, 0),
        Cursor = new Cursor(StandardCursorType.BottomRightCorner)
    };

    private void OnTilePressed(TilePlacement placement, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(placement.Container).Properties;
        if (properties.IsRightButtonPressed)
        {
            ShowMenu(placement.Tile, e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed) return;

        CloseMenu();
        placement.Container.ZIndex = ++topZ;

        // Handled here rather than through DoubleTapped: the drag below marks the press handled,
        // which stops the gesture recogniser ever seeing the second click.
        if (e.ClickCount >= 2)
        {
            e.Handled = true;
            TileActivated?.Invoke(placement.Tile);
            return;
        }

        draggingTile = placement;
        dragPointerStart = e.GetPosition(world);
        dragTileStart = placement.Position;
        e.Pointer.Capture(placement.Container);
        e.Handled = true;
    }

    private void OnTileMoved(TilePlacement placement, PointerEventArgs e)
    {
        if (draggingTile != placement) return;
        var current = e.GetPosition(world);
        Canvas.SetLeft(placement.Container, dragTileStart.X + (current.X - dragPointerStart.X));
        Canvas.SetTop(placement.Container, dragTileStart.Y + (current.Y - dragPointerStart.Y));
    }

    private void OnTileReleased(TilePlacement placement, PointerReleasedEventArgs e)
    {
        if (draggingTile != placement) return;
        draggingTile = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        TileMoved?.Invoke(placement.Tile);
    }

    private void OnGripPressed(TilePlacement placement, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(placement.Container).Properties.IsLeftButtonPressed) return;
        CloseMenu();
        resizingTile = placement;
        resizePointerStart = e.GetPosition(world);
        resizeStart = placement.Size;
        e.Pointer.Capture((IInputElement?)e.Source);
        e.Handled = true;
    }

    private void OnGripMoved(TilePlacement placement, PointerEventArgs e)
    {
        if (resizingTile != placement) return;
        var current = e.GetPosition(world);
        placement.Container.Width =
            Math.Max(MinTileWidth, resizeStart.Width + (current.X - resizePointerStart.X));
        placement.Container.Height =
            Math.Max(MinTileHeight, resizeStart.Height + (current.Y - resizePointerStart.Y));
        e.Handled = true;
    }

    private void OnGripReleased(TilePlacement placement, PointerReleasedEventArgs e)
    {
        if (resizingTile != placement) return;
        resizingTile = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseMenu();
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        panning = true;
        panPointerStart = e.GetPosition(this);
        panStart = new Point(pan.X, pan.Y);
        e.Pointer.Capture(this);
    }

    private void OnBackgroundMoved(object? sender, PointerEventArgs e)
    {
        if (!panning) return;
        var current = e.GetPosition(this);
        pan.X = panStart.X + (current.X - panPointerStart.X);
        pan.Y = panStart.Y + (current.Y - panPointerStart.Y);
    }

    private void OnBackgroundReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!panning) return;
        panning = false;
        e.Pointer.Capture(null);
    }

    private void ShowMenu(DashboardTile tile, Point viewportPoint)
    {
        if (MenuProvider is null) return;
        var items = MenuProvider(tile);
        if (items.Count == 0) return;

        menuLayer.Children.Clear();
        var menu = CanvasMenu.Build(tile.Name, items, CloseMenu);
        var estimatedHeight = CanvasMenu.EstimateHeight(items.Count);
        Canvas.SetLeft(menu, Math.Max(0, Math.Min(viewportPoint.X, Bounds.Width - CanvasMenu.Width - 10)));
        Canvas.SetTop(menu, Math.Max(0, Math.Min(viewportPoint.Y, Bounds.Height - estimatedHeight)));
        menuLayer.Children.Add(menu);
        menuLayer.IsVisible = true;
    }

    private void CloseMenu()
    {
        if (!menuLayer.IsVisible) return;
        menuLayer.IsVisible = false;
        menuLayer.Children.Clear();
    }

    private void OnMenuLayerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source != menuLayer) return;
        CloseMenu();
        e.Handled = true;
    }
}
