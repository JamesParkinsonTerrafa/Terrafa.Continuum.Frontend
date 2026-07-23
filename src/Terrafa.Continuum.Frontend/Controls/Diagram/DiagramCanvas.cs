using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Diagram;

public sealed class DiagramNode
{
    internal DiagramNode(string id, NodeCard card, Panel container)
    {
        Id = id;
        Card = card;
        Container = container;
    }

    public string Id { get; }
    public NodeCard Card { get; }
    internal Panel Container { get; }
    internal PortMarker? LeftPort { get; set; }
    internal PortMarker? RightPort { get; set; }
    public bool HasLeftPort => LeftPort is not null;
    public bool HasRightPort => RightPort is not null;
}

public sealed class DiagramConnection
{
    internal DiagramConnection(DiagramNode source, DiagramNode target)
    {
        Source = source;
        Target = target;
    }

    public DiagramNode Source { get; }
    public DiagramNode Target { get; }
}

public class DiagramCanvas : Border
{
    private const double EdgeLayerExtent = 6000;
    private const double EdgeLayerOffset = 3000;
    private const double PortGrabRadius = 16;

    private readonly Canvas world;
    private readonly EdgeLayer edgeLayer;
    private readonly Canvas menuLayer;
    private readonly TranslateTransform pan = new();
    private readonly List<DiagramNode> nodes = [];
    private readonly List<DiagramConnection> connections = [];

    private DiagramNode? draggingNode;
    private Point dragPointerStart;
    private Point dragNodeStart;
    private bool panning;
    private Point panPointerStart;
    private Point panStart;
    private (DiagramNode Node, PortSide Side)? pendingConnection;
    private Point pendingPointer;
    private int topZ;

    public Func<DiagramNode, IReadOnlyList<(string Label, Action Action)>>? MenuProvider { get; set; }

    public Func<DiagramNode, DiagramNode, (IBrush Stroke, double[]? Dashes, double Opacity)> ConnectionStyle { get; set; } =
        (_, _) => (Palette.Cyan, null, 0.7);

    public IReadOnlyList<DiagramNode> Nodes => nodes;
    public IReadOnlyList<DiagramConnection> Connections => connections;

    public DiagramCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;

        edgeLayer = new EdgeLayer { Width = EdgeLayerExtent, Height = EdgeLayerExtent };
        Canvas.SetLeft(edgeLayer, -EdgeLayerOffset);
        Canvas.SetTop(edgeLayer, -EdgeLayerOffset);

        world = new Canvas { RenderTransform = pan };
        world.Children.Add(edgeLayer);

        menuLayer = new Canvas { IsVisible = false, Background = Brushes.Transparent };
        menuLayer.PointerPressed += OnMenuLayerPressed;

        Child = new Panel { Children = { world, menuLayer } };

        PointerPressed += OnBackgroundPressed;
        PointerMoved += OnBackgroundMoved;
        PointerReleased += OnBackgroundReleased;
    }

    public DiagramNode AddNode(string id, NodeCard card, bool leftPort, bool rightPort, Point position)
    {
        var container = new Panel { Background = Brushes.Transparent };
        container.Children.Add(card);
        var node = new DiagramNode(id, card, container);

        if (leftPort) node.LeftPort = AttachPort(node, PortSide.Left);
        if (rightPort) node.RightPort = AttachPort(node, PortSide.Right);

        Canvas.SetLeft(container, position.X);
        Canvas.SetTop(container, position.Y);
        container.ZIndex = ++topZ;
        container.Cursor = new Cursor(StandardCursorType.Hand);

        container.PointerPressed += (_, e) => OnNodePressed(node, e);
        container.PointerMoved += (_, e) => OnNodeMoved(node, e);
        container.PointerReleased += (_, e) => OnNodeReleased(node, e);
        container.SizeChanged += (_, _) => RefreshEdges();

        world.Children.Add(container);
        nodes.Add(node);
        RefreshEdges();
        return node;
    }

    public void RemoveNode(DiagramNode node)
    {
        connections.RemoveAll(connection => connection.Source == node || connection.Target == node);
        world.Children.Remove(node.Container);
        nodes.Remove(node);
        RefreshEdges();
    }

    public void Connect(DiagramNode source, DiagramNode target)
    {
        if (source == target) return;
        if (!source.HasRightPort || !target.HasLeftPort) return;
        if (connections.Any(connection => connection.Source == source && connection.Target == target)) return;
        connections.Add(new DiagramConnection(source, target));
        RefreshEdges();
    }

    public Point ViewportToWorld(Point viewportPoint) => new(viewportPoint.X - pan.X, viewportPoint.Y - pan.Y);

    public Point WorldToViewport(Point worldPoint) => new(worldPoint.X + pan.X, worldPoint.Y + pan.Y);

    public Point NodeCenter(DiagramNode node)
    {
        var position = NodePosition(node);
        var size = node.Container.Bounds.Size;
        return new Point(position.X + size.Width / 2, position.Y + size.Height / 2);
    }

    public Point PortAnchor(DiagramNode node, PortSide side)
    {
        var position = NodePosition(node);
        var size = node.Container.Bounds.Size;
        var middle = position.Y + size.Height / 2;
        return side == PortSide.Right
            ? new Point(position.X + size.Width + PortMarker.Bulge, middle)
            : new Point(position.X - PortMarker.Bulge, middle);
    }

    private static Point NodePosition(DiagramNode node) =>
        new(Canvas.GetLeft(node.Container), Canvas.GetTop(node.Container));

    private PortMarker AttachPort(DiagramNode node, PortSide side)
    {
        var port = new PortMarker(side, NodeCard.AccentFor(node.Card.Variant))
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = side == PortSide.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = side == PortSide.Left
                ? new Thickness(-PortMarker.MarkerWidth, 0, 0, 0)
                : new Thickness(0, 0, -PortMarker.MarkerWidth, 0),
            Cursor = new Cursor(StandardCursorType.Cross)
        };
        port.PointerPressed += (_, e) => OnPortPressed(node, side, e);
        port.PointerMoved += (_, e) => OnPortMoved(e);
        port.PointerReleased += (_, e) => OnPortReleased(node, side, e);
        node.Container.Children.Add(port);
        return port;
    }

    private void OnNodePressed(DiagramNode node, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(node.Container).Properties;
        if (properties.IsRightButtonPressed)
        {
            ShowMenu(node, e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed) return;
        CloseMenu();
        draggingNode = node;
        dragPointerStart = e.GetPosition(world);
        dragNodeStart = NodePosition(node);
        node.Container.ZIndex = ++topZ;
        e.Pointer.Capture(node.Container);
        e.Handled = true;
    }

    private void OnNodeMoved(DiagramNode node, PointerEventArgs e)
    {
        if (draggingNode != node) return;
        var current = e.GetPosition(world);
        Canvas.SetLeft(node.Container, dragNodeStart.X + (current.X - dragPointerStart.X));
        Canvas.SetTop(node.Container, dragNodeStart.Y + (current.Y - dragPointerStart.Y));
        RefreshEdges();
    }

    private void OnNodeReleased(DiagramNode node, PointerReleasedEventArgs e)
    {
        if (draggingNode != node) return;
        draggingNode = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPortPressed(DiagramNode node, PortSide side, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(node.Container).Properties.IsLeftButtonPressed) return;
        CloseMenu();
        pendingConnection = (node, side);
        pendingPointer = e.GetPosition(world);
        e.Pointer.Capture(side == PortSide.Left ? node.LeftPort : node.RightPort);
        e.Handled = true;
        RefreshEdges();
    }

    private void OnPortMoved(PointerEventArgs e)
    {
        if (pendingConnection is null) return;
        pendingPointer = e.GetPosition(world);
        RefreshEdges();
    }

    private void OnPortReleased(DiagramNode node, PortSide side, PointerReleasedEventArgs e)
    {
        if (pendingConnection is null) return;
        var dropPoint = e.GetPosition(world);
        var oppositeSide = side == PortSide.Right ? PortSide.Left : PortSide.Right;
        var target = FindPortAt(dropPoint, oppositeSide, node);
        if (target is not null)
        {
            var source = side == PortSide.Right ? node : target;
            var sink = side == PortSide.Right ? target : node;
            Connect(source, sink);
        }
        pendingConnection = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        RefreshEdges();
    }

    private DiagramNode? FindPortAt(Point worldPoint, PortSide side, DiagramNode exclude)
    {
        foreach (var candidate in nodes)
        {
            if (candidate == exclude) continue;
            if (side == PortSide.Left && !candidate.HasLeftPort) continue;
            if (side == PortSide.Right && !candidate.HasRightPort) continue;
            var anchor = PortAnchor(candidate, side);
            var dx = anchor.X - worldPoint.X;
            var dy = anchor.Y - worldPoint.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= PortGrabRadius) return candidate;
        }
        return null;
    }

    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CloseMenu();
            return;
        }
        CloseMenu();
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

    private void RefreshEdges()
    {
        var edges = new List<Edge>();
        foreach (var connection in connections)
        {
            var (stroke, dashes, opacity) = ConnectionStyle(connection.Source, connection.Target);
            edges.Add(new Edge
            {
                From = ToEdgeSpace(PortAnchor(connection.Source, PortSide.Right)),
                To = ToEdgeSpace(PortAnchor(connection.Target, PortSide.Left)),
                Stroke = stroke,
                Dashes = dashes,
                Opacity = opacity,
                ArrowAtEnd = true
            });
        }
        if (pendingConnection is { } pending)
        {
            var anchor = PortAnchor(pending.Node, pending.Side);
            edges.Add(new Edge
            {
                From = ToEdgeSpace(pending.Side == PortSide.Right ? anchor : pendingPointer),
                To = ToEdgeSpace(pending.Side == PortSide.Right ? pendingPointer : anchor),
                Stroke = Palette.Amber,
                Dashes = [4, 3],
                Thickness = 1.2,
                Opacity = 0.9,
                ArrowAtEnd = true
            });
        }
        edgeLayer.Edges = edges;
    }

    private static Point ToEdgeSpace(Point worldPoint) =>
        new(worldPoint.X + EdgeLayerOffset, worldPoint.Y + EdgeLayerOffset);

    private void ShowMenu(DiagramNode node, Point viewportPoint)
    {
        if (MenuProvider is null) return;
        var items = MenuProvider(node);
        if (items.Count == 0) return;

        menuLayer.Children.Clear();
        var stack = new StackPanel();
        var header = node.Card.Title.Length > 0 ? node.Card.Title : node.Card.TagText;
        stack.Children.Add(new Border
        {
            Padding = new Thickness(12, 7, 12, 5),
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = header.ToUpperInvariant(),
                FontSize = 9,
                LetterSpacing = 1,
                Foreground = Palette.TextFaint
            }
        });

        foreach (var (label, action) in items)
        {
            var itemText = new TextBlock
            {
                Text = label,
                FontSize = 10,
                LetterSpacing = 1,
                Foreground = Palette.Text
            };
            var item = new Border
            {
                Padding = new Thickness(12, 7),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = itemText
            };
            var itemAction = action;
            item.PointerEntered += (_, _) =>
            {
                item.Background = Palette.BgField;
                itemText.Foreground = Palette.Amber;
            };
            item.PointerExited += (_, _) =>
            {
                item.Background = Brushes.Transparent;
                itemText.Foreground = Palette.Text;
            };
            item.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                CloseMenu();
                itemAction();
            };
            stack.Children.Add(item);
        }

        var menu = new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.BorderMid,
            BorderThickness = new Thickness(1),
            MinWidth = 190,
            Child = stack
        };
        var estimatedHeight = 30 + items.Count * 30;
        Canvas.SetLeft(menu, Math.Max(0, Math.Min(viewportPoint.X, Bounds.Width - 200)));
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
        if (e.Source == menuLayer)
        {
            CloseMenu();
            e.Handled = true;
        }
    }
}
