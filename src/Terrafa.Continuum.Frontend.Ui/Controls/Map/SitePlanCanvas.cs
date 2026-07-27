using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Map;

public enum MapPinKind
{
    Measure,
    Figure,
    Provisional
}

/// <summary>
/// A value stuck to a point on the plan. <see cref="Anchor"/> is normalized to the image, not
/// to the canvas: the client swaps in a taller photo, or the window resizes, and every pin is
/// still over the same piece of concrete. <see cref="Leader"/> is the card's offset from that
/// point in canvas pixels, so a card keeps its size and its distance from what it labels.
/// </summary>
public sealed class MapPin
{
    internal MapPin(string id, string title, MapPinKind kind, NodeCard card, Panel container, Control marker, Rectangle ring)
    {
        Id = id;
        Title = title;
        Kind = kind;
        Card = card;
        Container = container;
        Marker = marker;
        Ring = ring;
    }

    public string Id { get; }
    public string Title { get; }
    public MapPinKind Kind { get; }
    public NodeCard Card { get; }
    public string Detail { get; init; } = "";
    public Point Anchor { get; internal set; }
    public Vector Leader { get; internal set; }

    internal Panel Container { get; }
    internal Control Marker { get; }
    internal Rectangle Ring { get; }
}

/// <summary>A framed region of the plan — normalized like a pin, and rotated to sit square to
/// whatever it frames.</summary>
public sealed class MapZone
{
    internal MapZone(string label, Rect area, Border shell, TextBlock labelBlock)
    {
        Label = label;
        Area = area;
        Shell = shell;
        LabelBlock = labelBlock;
    }

    public string Label { get; }
    public Rect Area { get; }

    internal Border Shell { get; }
    internal TextBlock LabelBlock { get; }
}

/// <summary>
/// The client's site image with figures pinned to it. The image is either the generated
/// <see cref="SitePlanPlaceholder"/> or a bitmap the client uploaded; everything laid over it
/// is anchored in normalized image coordinates, so replacing the image keeps the composition.
/// </summary>
public sealed class SitePlanCanvas : Border
{
    private const double Inset = 18;
    private const double MarkerRadius = 5.5;

    private readonly Canvas planLayer = new() { IsHitTestVisible = false };
    private readonly Canvas zoneLayer = new() { IsHitTestVisible = false };
    private readonly EdgeLayer flowLayer = new() { IsHitTestVisible = false };
    private readonly EdgeLayer leaderLayer = new() { IsHitTestVisible = false };
    private readonly Canvas markerLayer = new() { IsHitTestVisible = false };
    private readonly Canvas pinLayer = new();
    private readonly Canvas menuLayer;

    private readonly SitePlanPlaceholder placeholder = new();
    private readonly Image imageHost = new() { Stretch = Stretch.Fill };
    private readonly Rectangle frame = new() { StrokeThickness = 1 };
    private readonly TextBlock caption;
    private readonly Border noteShell;
    private readonly TextBlock noteBlock;

    private readonly List<MapPin> pins = [];
    private readonly List<MapZone> zones = [];

    private Bitmap? image;
    private Rect planRect;
    private (Point From, Point To)? flow;
    private Point notePosition;
    private MapPin? dragging;
    private Vector dragGrab;
    private bool dropActive;
    private bool showLabels = true;
    private int topZ;

    public SitePlanCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;

        imageHost.IsVisible = false;
        planLayer.Children.Add(placeholder);
        planLayer.Children.Add(imageHost);
        planLayer.Children.Add(frame);

        caption = new TextBlock
        {
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            Background = Palette.ZoneLabelBackdrop,
            Padding = new Thickness(6, 2),
            Text = "PLACEHOLDER · GENERATED SITE PLAN"
        };
        caption.SizeChanged += (_, _) => PlaceCaption();
        planLayer.Children.Add(caption);

        noteBlock = new TextBlock { FontSize = 9, Foreground = Palette.Purple };
        noteShell = new Border
        {
            Background = Palette.CanvasNoteBackdrop,
            BorderBrush = Palette.Purple,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3),
            IsVisible = false,
            Child = noteBlock
        };

        var flowHost = new Canvas { IsHitTestVisible = false };
        flowHost.Children.Add(noteShell);

        menuLayer = new Canvas { IsVisible = false, Background = Brushes.Transparent };
        menuLayer.PointerPressed += OnMenuLayerPressed;

        Child = new Panel
        {
            Children = { planLayer, zoneLayer, flowLayer, flowHost, leaderLayer, markerLayer, pinLayer, menuLayer }
        };

        // Only bare plan presses reach this: pins handle their own, and every other layer is
        // hit-test invisible. An open menu covers the canvas and dismisses itself first.
        PointerPressed += (_, _) => Select(null);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropActive(false));
        AddHandler(DragDrop.DropEvent, OnDrop);
        UpdateFrame();
    }

    /// <summary>Raised when a file is dropped on the plan. The view owns decoding, because it
    /// owns the status line that has to report a file it could not read.</summary>
    public event Action<IStorageFile>? FileDropped;

    public event Action<MapPin?>? SelectionChanged;

    public Func<MapPin, IReadOnlyList<(string Label, Action Action)>>? MenuProvider { get; set; }

    public IReadOnlyList<MapPin> Pins => pins;

    public MapPin? Selected { get; private set; }

    /// <summary>The on-screen rectangle the image occupies — the frame every anchor is read against.</summary>
    public Rect PlanRect => planRect;

    public bool ShowImage
    {
        get => planLayer.IsVisible;
        set => planLayer.IsVisible = value;
    }

    public bool ShowZones
    {
        get => zoneLayer.IsVisible;
        set => zoneLayer.IsVisible = value;
    }

    public bool ShowPins
    {
        get => pinLayer.IsVisible;
        set
        {
            pinLayer.IsVisible = value;
            markerLayer.IsVisible = value;
            leaderLayer.IsVisible = value;
        }
    }

    public bool ShowFlows
    {
        get => flowLayer.IsVisible;
        set
        {
            flowLayer.IsVisible = value;
            noteShell.IsVisible = value && flow is not null;
        }
    }

    public bool ShowLabels
    {
        get => showLabels;
        set
        {
            showLabels = value;
            foreach (var zone in zones) zone.LabelBlock.IsVisible = value;
            caption.IsVisible = value && image is null;
        }
    }

    /// <summary>Hands the plan a client image, or null to fall back to the generated one. The
    /// canvas takes ownership: the previous bitmap is disposed here.</summary>
    public void SetImage(Bitmap? bitmap)
    {
        if (ReferenceEquals(bitmap, image)) return;
        image?.Dispose();
        image = bitmap;

        imageHost.Source = bitmap;
        imageHost.IsVisible = bitmap is not null;
        placeholder.IsVisible = bitmap is null;
        caption.IsVisible = bitmap is null && showLabels;

        planRect = default;
        InvalidateArrange();
    }

    public void AddZone(string label, Point centre, Size size, double angle, IBrush stroke, IBrush fill)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = stroke,
            Background = Palette.ZoneLabelBackdrop,
            Padding = new Thickness(6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 4),
            IsVisible = ShowLabels
        };
        var shell = new Border
        {
            BorderBrush = stroke,
            BorderThickness = new Thickness(1.5),
            Background = fill,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new RotateTransform(angle),
            Child = labelBlock
        };
        var area = new Rect(centre.X - size.Width / 2, centre.Y - size.Height / 2, size.Width, size.Height);
        var zone = new MapZone(label, area, shell, labelBlock);
        zones.Add(zone);
        zoneLayer.Children.Add(shell);
        PlaceZone(zone);
    }

    public MapPin AddPin(string id, string title, MapPinKind kind, NodeCard card, Point anchor, Vector leader, string detail = "")
    {
        var ring = new Rectangle
        {
            Stroke = Palette.Amber,
            StrokeThickness = 1,
            StrokeDashArray = [3, 3],
            Fill = Brushes.Transparent,
            Margin = new Thickness(-5),
            IsVisible = false
        };
        var container = new Panel { Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        container.Children.Add(ring);
        container.Children.Add(card);

        var accent = AccentFor(kind);
        var marker = new Panel
        {
            Width = MarkerRadius * 4,
            Height = MarkerRadius * 4,
            Children =
            {
                new Ellipse
                {
                    Width = MarkerRadius * 2,
                    Height = MarkerRadius * 2,
                    Stroke = accent,
                    StrokeThickness = 1.3,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new Ellipse
                {
                    Width = 3.5,
                    Height = 3.5,
                    Fill = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var pin = new MapPin(id, title, kind, card, container, marker, ring)
        {
            Anchor = Clamp(anchor),
            Leader = leader,
            Detail = detail
        };

        container.ZIndex = ++topZ;
        container.PointerPressed += (_, e) => OnPinPressed(pin, e);
        container.PointerMoved += (_, e) => OnPinMoved(pin, e);
        container.PointerReleased += (_, e) => OnPinReleased(pin, e);
        container.PointerCaptureLost += (_, _) => dragging = null;
        container.SizeChanged += (_, _) => RefreshLeaders();

        pins.Add(pin);
        pinLayer.Children.Add(container);
        markerLayer.Children.Add(marker);
        PlacePin(pin);
        RefreshLeaders();
        return pin;
    }

    public void RemovePin(MapPin pin)
    {
        if (!pins.Remove(pin)) return;
        pinLayer.Children.Remove(pin.Container);
        markerLayer.Children.Remove(pin.Marker);
        if (Selected == pin) Select(null);
        RefreshLeaders();
    }

    public MapPin? FindPin(string id) => pins.FirstOrDefault(pin => pin.Id == id);

    public void SetFlow(Point from, Point to, string note, Point notePoint)
    {
        flow = (Clamp(from), Clamp(to));
        notePosition = Clamp(notePoint);
        noteBlock.Text = note;
        noteShell.IsVisible = ShowFlows;
        RefreshFlow();
    }

    public void Select(MapPin? pin)
    {
        if (Selected == pin) return;
        if (Selected is not null) Selected.Ring.IsVisible = false;
        Selected = pin;
        if (pin is not null)
        {
            pin.Ring.IsVisible = true;
            pin.Container.ZIndex = ++topZ;
        }
        SelectionChanged?.Invoke(pin);
    }

    public bool ContainsPlanPoint(Point canvasPoint) => planRect.Width > 0 && planRect.Contains(canvasPoint);

    public Point ToNormalized(Point canvasPoint) => planRect.Width <= 0 || planRect.Height <= 0
        ? new Point(0.5, 0.5)
        : Clamp(new Point(
            (canvasPoint.X - planRect.X) / planRect.Width,
            (canvasPoint.Y - planRect.Y) / planRect.Height));

    private Point ToCanvas(Point normalized) =>
        new(planRect.X + normalized.X * planRect.Width, planRect.Y + normalized.Y * planRect.Height);

    public static IBrush AccentFor(MapPinKind kind) => kind switch
    {
        MapPinKind.Measure => Palette.Cyan,
        MapPinKind.Provisional => Palette.Purple,
        _ => Palette.Green
    };

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        var rect = ComputePlanRect(size);
        if (rect != planRect)
        {
            planRect = rect;
            Relayout();
        }
        return size;
    }

    private Rect ComputePlanRect(Size size)
    {
        var width = size.Width - Inset * 2;
        var height = size.Height - Inset * 2;
        if (width <= 0 || height <= 0) return default;

        var aspect = image is { } bitmap && bitmap.Size.Height > 0
            ? bitmap.Size.Width / bitmap.Size.Height
            : SitePlanPlaceholder.Aspect;

        var planWidth = width;
        var planHeight = planWidth / aspect;
        if (planHeight > height)
        {
            planHeight = height;
            planWidth = planHeight * aspect;
        }
        return new Rect(
            Inset + (width - planWidth) / 2,
            Inset + (height - planHeight) / 2,
            planWidth,
            planHeight);
    }

    private void Relayout()
    {
        var plan = (Control)(image is null ? placeholder : imageHost);
        plan.Width = planRect.Width;
        plan.Height = planRect.Height;
        Canvas.SetLeft(plan, planRect.X);
        Canvas.SetTop(plan, planRect.Y);

        frame.Width = planRect.Width;
        frame.Height = planRect.Height;
        Canvas.SetLeft(frame, planRect.X);
        Canvas.SetTop(frame, planRect.Y);

        PlaceCaption();

        foreach (var zone in zones) PlaceZone(zone);
        foreach (var pin in pins) PlacePin(pin);
        RefreshLeaders();
        RefreshFlow();
    }

    private void PlaceCaption()
    {
        Canvas.SetLeft(caption, planRect.Right - caption.Bounds.Width - 8);
        Canvas.SetTop(caption, planRect.Bottom - caption.Bounds.Height - 8);
    }

    private void PlaceZone(MapZone zone)
    {
        zone.Shell.Width = zone.Area.Width * planRect.Width;
        zone.Shell.Height = zone.Area.Height * planRect.Height;
        Canvas.SetLeft(zone.Shell, planRect.X + zone.Area.X * planRect.Width);
        Canvas.SetTop(zone.Shell, planRect.Y + zone.Area.Y * planRect.Height);
    }

    private void PlacePin(MapPin pin)
    {
        var anchor = ToCanvas(pin.Anchor);
        Canvas.SetLeft(pin.Container, anchor.X + pin.Leader.X);
        Canvas.SetTop(pin.Container, anchor.Y + pin.Leader.Y);
        Canvas.SetLeft(pin.Marker, anchor.X - MarkerRadius * 2);
        Canvas.SetTop(pin.Marker, anchor.Y - MarkerRadius * 2);
    }

    private void RefreshLeaders()
    {
        var edges = new List<Edge>();
        foreach (var pin in pins)
        {
            var anchor = ToCanvas(pin.Anchor);
            var card = new Rect(
                anchor.X + pin.Leader.X,
                anchor.Y + pin.Leader.Y,
                pin.Container.Bounds.Width,
                pin.Container.Bounds.Height);
            var attach = new Point(
                Math.Clamp(anchor.X, card.X, card.Right),
                Math.Clamp(anchor.Y, card.Y, card.Bottom));
            var reach = attach - anchor;
            if (Math.Abs(reach.X) + Math.Abs(reach.Y) < MarkerRadius * 2.5) continue;

            edges.Add(new Edge
            {
                From = anchor,
                To = attach,
                Stroke = AccentFor(pin.Kind),
                Thickness = 1,
                Dashes = [3, 3],
                Opacity = 0.65
            });
        }
        leaderLayer.Edges = edges;
    }

    private void RefreshFlow()
    {
        if (flow is not { } line)
        {
            flowLayer.Edges = [];
            return;
        }
        flowLayer.Edges =
        [
            new Edge
            {
                From = ToCanvas(line.From),
                To = ToCanvas(line.To),
                Stroke = Palette.Purple,
                Dashes = [7, 5],
                ArrowAtEnd = true
            }
        ];
        var note = ToCanvas(notePosition);
        Canvas.SetLeft(noteShell, note.X);
        Canvas.SetTop(noteShell, note.Y);
    }

    private void OnPinPressed(MapPin pin, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            Select(pin);
            ShowMenu(pin, e.GetPosition(this));
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed) return;

        CloseMenu();
        Select(pin);
        dragging = pin;
        dragGrab = e.GetPosition(this) - ToCanvas(pin.Anchor);
        e.Pointer.Capture(pin.Container);
        e.Handled = true;
    }

    private void OnPinMoved(MapPin pin, PointerEventArgs e)
    {
        if (dragging != pin) return;
        pin.Anchor = ToNormalized(e.GetPosition(this) - dragGrab);
        PlacePin(pin);
        RefreshLeaders();
        SelectionChanged?.Invoke(pin);
    }

    private void OnPinReleased(MapPin pin, PointerReleasedEventArgs e)
    {
        if (dragging != pin) return;
        dragging = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ShowMenu(MapPin pin, Point point)
    {
        if (MenuProvider is null) return;
        var items = MenuProvider(pin);
        if (items.Count == 0) return;

        menuLayer.Children.Clear();
        var menu = CanvasMenu.Build(pin.Title, items, CloseMenu);
        Canvas.SetLeft(menu, Math.Max(0, Math.Min(point.X, Bounds.Width - CanvasMenu.Width - 10)));
        Canvas.SetTop(menu, Math.Max(0, Math.Min(point.Y, Bounds.Height - CanvasMenu.EstimateHeight(items.Count))));
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFile = DroppedFile(e) is not null;
        e.DragEffects = hasFile ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropActive(hasFile);
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropActive(false);
        if (DroppedFile(e) is not { } file) return;
        e.Handled = true;
        FileDropped?.Invoke(file);
    }

    private static IStorageFile? DroppedFile(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();

    private void SetDropActive(bool active)
    {
        if (dropActive == active) return;
        dropActive = active;
        UpdateFrame();
    }

    private void UpdateFrame()
    {
        frame.Stroke = dropActive ? Palette.Amber : Palette.BorderMid;
        frame.StrokeThickness = dropActive ? 2 : 1;
        frame.StrokeDashArray = dropActive ? [6, 4] : null;
    }

    private static Point Clamp(Point normalized) =>
        new(Math.Clamp(normalized.X, 0, 1), Math.Clamp(normalized.Y, 0, 1));
}
