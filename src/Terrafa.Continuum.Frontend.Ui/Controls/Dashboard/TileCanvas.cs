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
    private const double MinTileWidth = Models.Dashboard.MinTileWidth;
    private const double MinTileHeight = Models.Dashboard.MinTileHeight;
    private const double GripSize = 14;
    private const double MinZoom = 0.4;
    private const double MaxZoom = 2.5;

    private readonly Canvas world;
    private readonly Canvas menuLayer;
    private readonly GridLayer grid;
    private readonly TranslateTransform pan = new();
    private readonly ScaleTransform zoom = new();
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

        // Scale before translate, so the pan stays in viewport pixels whatever the zoom.
        world = new Canvas
        {
            RenderTransform = new TransformGroup { Children = { zoom, pan } },
            RenderTransformOrigin = RelativePoint.TopLeft
        };
        menuLayer = new Canvas { IsVisible = false, Background = Brushes.Transparent };
        menuLayer.PointerPressed += OnMenuLayerPressed;

        grid = new GridLayer(pan, zoom) { IsHitTestVisible = false, IsVisible = SnapSettings.ShowGridLines };
        Child = new Panel { Children = { grid, world, menuLayer } };

        PointerPressed += OnBackgroundPressed;
        PointerMoved += OnBackgroundMoved;
        PointerReleased += OnBackgroundReleased;
        PointerWheelChanged += OnWheel;
    }

    /// <summary>Zooms about the pointer: the spot under the cursor stays under the cursor.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var target = Math.Clamp(zoom.ScaleX * Math.Pow(1.12, e.Delta.Y), MinZoom, MaxZoom);
        if (Math.Abs(target - zoom.ScaleX) > 0.0001)
        {
            var anchor = e.GetPosition(this);
            var worldPoint = ViewportToWorld(anchor);
            zoom.ScaleX = target;
            zoom.ScaleY = target;
            pan.X = anchor.X - worldPoint.X * target;
            pan.Y = anchor.Y - worldPoint.Y * target;
            grid.InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SnapSettings.Changed += OnSnapSettingChanged;
        OnSnapSettingChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SnapSettings.Changed -= OnSnapSettingChanged;
    }

    private void OnSnapSettingChanged()
    {
        grid.IsVisible = SnapSettings.ShowGridLines;
        grid.InvalidateVisual();
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

    /// <summary>Moves a tile already on the canvas — the sync path when the board's geometry changes under it.</summary>
    public void Reposition(DashboardTile tile, Point position, Size size)
    {
        if (Find(tile) is not { } placement) return;
        Canvas.SetLeft(placement.Container, position.X);
        Canvas.SetTop(placement.Container, position.Y);
        placement.Container.Width = Math.Max(size.Width, MinTileWidth);
        placement.Container.Height = Math.Max(size.Height, MinTileHeight);
    }

    public Point ViewportToWorld(Point viewportPoint) =>
        new((viewportPoint.X - pan.X) / zoom.ScaleX, (viewportPoint.Y - pan.Y) / zoom.ScaleY);

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
        var x = dragTileStart.X + (current.X - dragPointerStart.X);
        var y = dragTileStart.Y + (current.Y - dragPointerStart.Y);
        // The magnetic feel: near a gridline the tile leans toward it, but the hard lock
        // waits for release so the drag never fights the pointer.
        if (SnapSettings.Enabled)
        {
            x = SnapSettings.Magnetize(x);
            y = SnapSettings.Magnetize(y);
        }
        Canvas.SetLeft(placement.Container, x);
        Canvas.SetTop(placement.Container, y);
    }

    private void OnTileReleased(TilePlacement placement, PointerReleasedEventArgs e)
    {
        if (draggingTile != placement) return;
        draggingTile = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (SnapSettings.Enabled)
        {
            Canvas.SetLeft(placement.Container, SnapSettings.Snap(Canvas.GetLeft(placement.Container)));
            Canvas.SetTop(placement.Container, SnapSettings.Snap(Canvas.GetTop(placement.Container)));
        }
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
        var width = Math.Max(MinTileWidth, resizeStart.Width + (current.X - resizePointerStart.X));
        var height = Math.Max(MinTileHeight, resizeStart.Height + (current.Y - resizePointerStart.Y));
        // It is the dragged edges that feel the grid, not the raw size — a tile whose left
        // edge sits on a line then keeps its right edge on one too.
        if (SnapSettings.Enabled)
        {
            var position = placement.Position;
            width = Math.Max(MinTileWidth, SnapSettings.Magnetize(position.X + width) - position.X);
            height = Math.Max(MinTileHeight, SnapSettings.Magnetize(position.Y + height) - position.Y);
        }
        placement.Container.Width = width;
        placement.Container.Height = height;
        e.Handled = true;
    }

    private void OnGripReleased(TilePlacement placement, PointerReleasedEventArgs e)
    {
        if (resizingTile != placement) return;
        resizingTile = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (SnapSettings.Enabled)
        {
            var position = placement.Position;
            placement.Container.Width =
                SnapSettings.SnapAtLeast(position.X + placement.Container.Width, position.X + MinTileWidth) - position.X;
            placement.Container.Height =
                SnapSettings.SnapAtLeast(position.Y + placement.Container.Height, position.Y + MinTileHeight) - position.Y;
        }
        TileMoved?.Invoke(placement.Tile);
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
        grid.InvalidateVisual();
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
