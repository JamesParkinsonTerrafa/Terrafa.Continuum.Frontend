using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class TransferFunctionView : UserControl
{
    private const double DomainStart = 0.34;
    private const double DomainEnd = 3.2;
    private const double DragThreshold = 6;
    private const double StageArrowX = 182;

    private sealed class FunctionDraft
    {
        public required string Name { get; set; }
        public List<LibraryFunction> Stages { get; } = [];
        public string? SavedName { get; set; }
        public bool HasUnsavedChanges { get; set; }
    }

    private readonly FunctionLibrary library = FunctionLibrary.Instance;
    private readonly List<FunctionDraft> drafts = [];
    private readonly List<Panel> stageRows = [];
    private int activeDraftIndex;
    private bool isSyncingNameBox;

    private LibraryFunction? pressedFunction;
    private Point pressOrigin;
    private bool libraryDragActive;
    private Border? dragGhost;
    private Border? openMenu;

    private Panel? draggingRow;
    private double stageDragStartY;
    private bool stageDragMoved;
    private TranslateTransform? draggingTransform;

    internal IReadOnlyList<Panel> StageRows => stageRows;

    private FunctionDraft ActiveDraft => drafts[activeDraftIndex];

    private List<LibraryFunction> Stages => ActiveDraft.Stages;

    public TransferFunctionView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public TransferFunctionView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        var initial = new FunctionDraft { Name = "fig.draft_h" };
        initial.Stages.Add(library.Primitives.First(function => function.Name == "square"));
        initial.Stages.Add(library.Primitives.First(function => function.Name == "reciprocal"));
        drafts.Add(initial);

        StackTabs.TabSelected += SelectDraft;
        StackTabs.TabCloseRequested += RequestCloseDraft;
        NameBox.TextChanged += OnNameBoxChanged;
        LibraryPanel.PointerPressed += OnLibraryPanelPressed;
        SaveButton.PointerPressed += OnSavePressed;
        Overlay.PointerPressed += OnOverlayPressed;
        DialogHost.PointerPressed += OnDialogHostPressed;

        RebuildLibrary();
        SyncActiveDraft();

        NoiseOverlay.Attach(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HintSettings.Changed += RebuildStack;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HintSettings.Changed -= RebuildStack;
    }

    private void RebuildLibrary()
    {
        LibraryList.Children.Clear();
        foreach (var function in library.Primitives)
            LibraryList.Children.Add(CreateLibraryEntry(function));
        foreach (var function in library.UserFunctions)
            LibraryList.Children.Add(CreateLibraryEntry(function));
    }

    private Border CreateLibraryEntry(LibraryFunction function)
    {
        var tag = new TextBlock
        {
            Classes = { "tag" },
            Text = function.IsPrimitive ? "PRIMITIVE" : "COMPOSITE · USER",
            Foreground = function.IsPrimitive ? Palette.TextMuted : Palette.Purple
        };
        var formula = new TextBlock
        {
            Text = $"{function.Name}: {function.DisplayFormula}",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = function.IsPrimitive ? Palette.TextMuted : Palette.PurpleSoft
        };
        var body = new StackPanel { Children = { tag, formula } };
        if (function.Note.Length > 0)
        {
            body.Children.Add(new TextBlock
            {
                Classes = { "tag" },
                Text = function.Note,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Palette.TextFaint,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var baseBackground = function.IsPrimitive ? Brushes.Transparent : (IBrush)Palette.PurpleFill;
        var entry = new Border
        {
            BorderBrush = function.IsPrimitive ? Palette.TextGhost : Palette.Purple,
            BorderThickness = new Thickness(1),
            Background = baseBackground,
            Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body
        };
        entry.PointerEntered += (_, _) => entry.Background = Palette.BgField;
        entry.PointerExited += (_, _) => entry.Background = baseBackground;
        entry.PointerPressed += (_, e) => OnLibraryEntryPressed(function, entry, e);
        entry.PointerMoved += (_, e) => OnLibraryEntryMoved(e);
        entry.PointerReleased += (_, e) => OnLibraryEntryReleased(e);
        return entry;
    }

    private void OnLibraryEntryPressed(LibraryFunction function, Border entry, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(entry).Properties;
        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ShowMenu(function.Name, e.GetPosition(this),
                ("ADD TO COMPOSITION STACK", () => InsertStage(function, Stages.Count)),
                ("CREATE FUNCTION", BeginNewFunction));
            return;
        }
        if (!properties.IsLeftButtonPressed) return;
        CloseMenu();
        pressedFunction = function;
        pressOrigin = e.GetPosition(this);
        libraryDragActive = false;
        e.Pointer.Capture(entry);
        e.Handled = true;
    }

    private void OnLibraryEntryMoved(PointerEventArgs e)
    {
        if (pressedFunction is null) return;
        var position = e.GetPosition(this);
        if (!libraryDragActive)
        {
            var dx = position.X - pressOrigin.X;
            var dy = position.Y - pressOrigin.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;
            libraryDragActive = true;
            ShowDragGhost(pressedFunction);
        }
        MoveDragGhost(position);
    }

    private void OnLibraryEntryReleased(PointerReleasedEventArgs e)
    {
        if (pressedFunction is null) return;
        var function = pressedFunction;
        pressedFunction = null;
        e.Pointer.Capture(null);
        HideDragGhost();
        if (!libraryDragActive) return;
        libraryDragActive = false;
        var columnPosition = e.GetPosition(StackColumn);
        var overStack = columnPosition.X >= 0 && columnPosition.X <= StackColumn.Bounds.Width &&
                        columnPosition.Y >= 0 && columnPosition.Y <= StackColumn.Bounds.Height;
        if (!overStack) return;
        InsertStage(function, InsertIndexFor(e.GetPosition(StackHost).Y));
    }

    private int InsertIndexFor(double y)
    {
        var index = 0;
        foreach (var row in stageRows)
            if (row.Bounds.Center.Y < y)
                index++;
        return index;
    }

    private void InsertStage(LibraryFunction function, int index)
    {
        Stages.Insert(Math.Clamp(index, 0, Stages.Count), function);
        OnStagesEdited();
    }

    private void RemoveStage(int index)
    {
        Stages.RemoveAt(index);
        OnStagesEdited();
    }

    private void OnStagesEdited()
    {
        ActiveDraft.HasUnsavedChanges = true;
        RebuildTabs();
        RebuildStack();
        RefreshResult();
    }

    private void RebuildStack()
    {
        StackHost.Children.Clear();
        stageRows.Clear();

        StackHost.Children.Add(new NodeCard
        {
            Variant = NodeCardVariant.Measure,
            TagText = "STAGE 1 · INPUT",
            TagRight = "x",
            Title = "measure x · tank_01.level (norm.)",
            TitleSize = 14
        });

        for (var i = 0; i < Stages.Count; i++)
        {
            StackHost.Children.Add(CreateArrow());
            var row = CreateStageRow(Stages[i], i);
            stageRows.Add(row);
            StackHost.Children.Add(row);
        }

        StackHost.Children.Add(CreateArrow());
        StackHost.Children.Add(new NodeCard
        {
            Variant = NodeCardVariant.Figure,
            TagText = $"STAGE {Stages.Count + 2} · OUTPUT",
            TagRight = "h(x)",
            Title = $"h(x) = {FunctionLibrary.ComposeFormula(Stages, "x")}",
            TitleSize = 15
        });

        StackFooter.Text = (Stages.Count, HintSettings.Enabled) switch
        {
            (0, true) => "stack is empty — drag a library function in, or right-click it to add · h(x) = x",
            (0, false) => "stack is empty · h(x) = x",
            (_, true) => $"stages apply top to bottom: h = {ChainText()} · σ propagation placeholder — variance trace not yet wired",
            (_, false) => $"h = {ChainText()}"
        };
    }

    private string ChainText() =>
        Stages.Count == 0 ? "identity" : string.Join(" ∘ ", Enumerable.Reverse(Stages).Select(stage => stage.Name));

    private static EdgeLayer CreateArrow() => new()
    {
        Height = 30,
        Edges =
        [
            new Edge
            {
                From = new Point(StageArrowX, 0),
                To = new Point(StageArrowX, 28),
                Stroke = Palette.TextGhost,
                Thickness = 2,
                ArrowAtEnd = true
            }
        ]
    };

    private Panel CreateStageRow(LibraryFunction function, int index)
    {
        var card = new NodeCard
        {
            Variant = function.IsPrimitive ? NodeCardVariant.Transfer : NodeCardVariant.Provisional,
            TagText = $"STAGE {index + 2} · {function.Name}",
            TagRight = "drag ↕",
            Title = $"{function.Name}(u) = {function.DisplayFormula}",
            TitleSize = 15
        };

        var removeText = new TextBlock { Text = "✕", FontSize = 10, Foreground = Palette.TextFaint };
        var remove = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = removeText
        };
        remove.PointerEntered += (_, _) => removeText.Foreground = Palette.Red;
        remove.PointerExited += (_, _) => removeText.Foreground = Palette.TextFaint;
        remove.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            RemoveStage(index);
        };

        var row = new Panel
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
        };
        row.Children.Add(card);
        row.Children.Add(remove);
        row.PointerPressed += (_, e) => OnStagePressed(row, function, index, e);
        row.PointerMoved += (_, e) => OnStageMoved(row, e);
        row.PointerReleased += (_, e) => OnStageReleased(row, index, e);
        return row;
    }

    private void OnStagePressed(Panel row, LibraryFunction function, int index, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(row).Properties;
        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ShowMenu($"STAGE {index + 2} · {function.Name}", e.GetPosition(this),
                ("REMOVE FROM STACK", () => RemoveStage(index)));
            return;
        }
        if (!properties.IsLeftButtonPressed) return;
        CloseMenu();
        draggingRow = row;
        stageDragStartY = e.GetPosition(StackHost).Y;
        stageDragMoved = false;
        draggingTransform = new TranslateTransform();
        row.RenderTransform = draggingTransform;
        row.ZIndex = 100;
        e.Pointer.Capture(row);
        e.Handled = true;
    }

    private void OnStageMoved(Panel row, PointerEventArgs e)
    {
        if (draggingRow != row || draggingTransform is null) return;
        var delta = e.GetPosition(StackHost).Y - stageDragStartY;
        if (Math.Abs(delta) > DragThreshold) stageDragMoved = true;
        draggingTransform.Y = delta;
        row.Opacity = 0.85;
    }

    private void OnStageReleased(Panel row, int index, PointerReleasedEventArgs e)
    {
        if (draggingRow != row) return;
        draggingRow = null;
        e.Pointer.Capture(null);
        row.Opacity = 1;
        var delta = draggingTransform?.Y ?? 0;
        row.RenderTransform = null;
        draggingTransform = null;
        if (!stageDragMoved)
        {
            row.ZIndex = 0;
            return;
        }
        var draggedCenter = row.Bounds.Center.Y + delta;
        var target = 0;
        for (var i = 0; i < stageRows.Count; i++)
        {
            if (i == index) continue;
            if (stageRows[i].Bounds.Center.Y < draggedCenter) target++;
        }
        if (target != index)
        {
            var moved = Stages[index];
            Stages.RemoveAt(index);
            Stages.Insert(target, moved);
        }
        OnStagesEdited();
    }

    private void OnLibraryPanelPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        ShowMenu("FUNCTION LIBRARY", e.GetPosition(this), ("CREATE FUNCTION", BeginNewFunction));
        e.Handled = true;
    }

    private void ShowMenu(string header, Point at, params (string Label, Action Action)[] items)
    {
        CloseMenu();

        var stack = new StackPanel();
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
            stack.Children.Add(CreateMenuItem(label, action));

        var menu = new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.BorderMid,
            BorderThickness = new Thickness(1),
            MinWidth = 210,
            Child = stack
        };
        var estimatedHeight = 30 + items.Length * 30;
        Canvas.SetLeft(menu, Math.Max(0, Math.Min(at.X, Bounds.Width - 220)));
        Canvas.SetTop(menu, Math.Max(0, Math.Min(at.Y, Bounds.Height - estimatedHeight)));
        Overlay.Children.Add(menu);
        Overlay.IsHitTestVisible = true;
        Overlay.IsVisible = true;
        openMenu = menu;
    }

    private Border CreateMenuItem(string label, Action action)
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
            action();
        };
        return item;
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source != Overlay) return;
        CloseMenu();
        e.Handled = true;
    }

    private void CloseMenu()
    {
        if (openMenu is null) return;
        Overlay.Children.Remove(openMenu);
        openMenu = null;
        if (Overlay.Children.Count == 0) Overlay.IsVisible = false;
    }

    private void ShowDialog(string header, string message, params (string Label, bool IsPrimary, Action Action)[] choices)
    {
        CloseMenu();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        foreach (var (label, isPrimary, action) in choices)
            buttons.Children.Add(CreateDialogButton(label, isPrimary, action));

        var card = new SquircleBorder
        {
            Classes = { "emboss-card" },
            Padding = new Thickness(24, 20),
            MinWidth = 360,
            MaxWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Classes = { "tag" },
                        Text = header.ToUpperInvariant(),
                        Foreground = Palette.Amber
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 13,
                        LineHeight = 19,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 0),
                        Foreground = Palette.Text
                    },
                    buttons
                }
            }
        };

        DialogHost.Children.Clear();
        DialogHost.Children.Add(card);
        DialogHost.IsVisible = true;
    }

    private Control CreateDialogButton(string label, bool isPrimary, Action action)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 10,
            LetterSpacing = 1,
            FontWeight = isPrimary ? FontWeight.Bold : FontWeight.Normal,
            Foreground = isPrimary ? Brushes.Black : Palette.TextSub
        };
        var key = new SquircleBorder
        {
            Classes = { isPrimary ? "emboss-key" : "emboss" },
            Padding = new Thickness(14, 6),
            Background = isPrimary ? Palette.Amber : Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text
        };
        key.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            CloseDialog();
            action();
        };
        return key;
    }

    private void OnDialogHostPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source != DialogHost) return;
        CloseDialog();
        e.Handled = true;
    }

    private void CloseDialog()
    {
        DialogHost.Children.Clear();
        DialogHost.IsVisible = false;
    }

    private void BeginNewFunction()
    {
        drafts.Add(new FunctionDraft { Name = NextDraftName() });
        activeDraftIndex = drafts.Count - 1;
        SyncActiveDraft();
    }

    private string NextDraftName()
    {
        var index = 1;
        while (library.FindUserFunction($"fn_{index}") is not null ||
               drafts.Any(draft => draft.Name == $"fn_{index}"))
            index++;
        return $"fn_{index}";
    }

    private void SelectDraft(int index)
    {
        if (index == activeDraftIndex || index < 0 || index >= drafts.Count) return;
        activeDraftIndex = index;
        SyncActiveDraft();
    }

    private void SyncActiveDraft()
    {
        isSyncingNameBox = true;
        NameBox.Text = ActiveDraft.Name;
        isSyncingNameBox = false;
        SetStatus($"editing: {ActiveDraft.Name}", false);
        RebuildTabs();
        RebuildStack();
        RefreshResult();
    }

    private void RebuildTabs()
    {
        StackTabs.Labels = drafts
            .Select(draft => draft.HasUnsavedChanges ? $"{draft.Name} ●" : draft.Name)
            .ToArray();
        StackTabs.ActiveIndex = activeDraftIndex;
    }

    private void OnNameBoxChanged(object? sender, TextChangedEventArgs e)
    {
        if (isSyncingNameBox) return;
        var name = NameBox.Text ?? "";
        if (name == ActiveDraft.Name) return;
        ActiveDraft.Name = name;
        ActiveDraft.HasUnsavedChanges = true;
        SetStatus($"editing: {name}", false);
        RebuildTabs();
        UpdateDraftBar();
    }

    private void RequestCloseDraft(int index)
    {
        if (index < 0 || index >= drafts.Count) return;
        var draft = drafts[index];
        if (!draft.HasUnsavedChanges)
        {
            CloseDraft(draft);
            return;
        }
        ShowDialog("UNSAVED CHANGES", $"Save changes to function {draft.Name}?",
            ("SAVE", true, () => SaveDraft(draft, () => CloseDraft(draft))),
            ("DISCARD", false, () => CloseDraft(draft)),
            ("CANCEL", false, () => { }));
    }

    private void CloseDraft(FunctionDraft draft)
    {
        var index = drafts.IndexOf(draft);
        if (index < 0) return;
        drafts.RemoveAt(index);
        if (drafts.Count == 0) drafts.Add(new FunctionDraft { Name = NextDraftName() });
        activeDraftIndex = Math.Clamp(activeDraftIndex >= index ? activeDraftIndex - 1 : activeDraftIndex,
            0, drafts.Count - 1);
        SyncActiveDraft();
    }

    private void OnSavePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        SaveDraft(ActiveDraft, null);
    }

    private void SaveDraft(FunctionDraft draft, Action? onSaved)
    {
        var name = draft.Name.Trim();
        if (name.Length == 0)
        {
            SetStatus("name the function before saving", true);
            return;
        }
        if (library.IsPrimitiveName(name))
        {
            SetStatus($"{name} is a primitive — choose another name", true);
            return;
        }
        if (draft.Stages.Count == 0)
        {
            SetStatus($"{name} has no stages — add at least one before saving", true);
            return;
        }
        if (library.FindUserFunction(name) is not null && name != draft.SavedName)
        {
            ShowDialog("OVERWRITE", $"{name} function already exists. Overwrite?",
                ("OVERWRITE", true, () => CommitDraft(draft, name, onSaved)),
                ("CANCEL", false, () => { }));
            return;
        }
        CommitDraft(draft, name, onSaved);
    }

    private void CommitDraft(FunctionDraft draft, string name, Action? onSaved)
    {
        library.SaveComposite(name, draft.Stages);
        draft.Name = name;
        draft.SavedName = name;
        draft.HasUnsavedChanges = false;
        RebuildLibrary();
        RebuildTabs();
        if (draft == ActiveDraft)
        {
            isSyncingNameBox = true;
            NameBox.Text = name;
            isSyncingNameBox = false;
        }
        SetStatus($"saved {name} → function library", false);
        UpdateDraftBar();
        onSaved?.Invoke();
    }

    private void SetStatus(string text, bool isError)
    {
        TopBarStatus.Text = text;
        TopBarStatus.Foreground = isError ? Palette.Red : Palette.TextMuted;
    }

    private void ShowDragGhost(LibraryFunction function)
    {
        dragGhost = new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.Amber,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            Opacity = 0.9,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"{function.Name}: {function.DisplayFormula}",
                FontSize = 11,
                Foreground = Palette.AmberSoft
            }
        };
        Overlay.Children.Add(dragGhost);
        Overlay.IsHitTestVisible = false;
        Overlay.IsVisible = true;
    }

    private void MoveDragGhost(Point position)
    {
        if (dragGhost is null) return;
        Canvas.SetLeft(dragGhost, position.X + 10);
        Canvas.SetTop(dragGhost, position.Y + 8);
    }

    private void HideDragGhost()
    {
        if (dragGhost is null) return;
        Overlay.Children.Remove(dragGhost);
        dragGhost = null;
        Overlay.IsHitTestVisible = true;
        if (Overlay.Children.Count == 0) Overlay.IsVisible = false;
    }

    private void RefreshResult()
    {
        double Compose(double x) => FunctionLibrary.ApplyStages(Stages, x);

        var trace = FunctionTrace.CreateTrace(Compose, DomainStart, DomainEnd);
        var (yMin, yMax) = FunctionTrace.RobustRange(trace);

        ResultChart.MarginLeft = 60;
        ResultChart.MarginRight = 20;
        ResultChart.MarginTop = 20;
        ResultChart.MarginBottom = 36;
        ResultChart.XMin = DomainStart;
        ResultChart.XMax = DomainEnd;
        ResultChart.YMin = yMin;
        ResultChart.YMax = yMax;

        var xTickValues = FunctionTrace.NiceSteps(DomainStart, DomainEnd, 6);
        ResultChart.VerticalGridValues = xTickValues;
        ResultChart.XTicks = xTickValues.Select(value => new AxisTick(value, FormatTick(value))).ToArray();

        var yGridValues = FunctionTrace.NiceSteps(yMin, yMax, 4);
        ResultChart.HorizontalGridValues = yGridValues;
        var labelX = DomainStart - (DomainEnd - DomainStart) * 0.015;
        ResultChart.Labels = yGridValues
            .Select(value => new ChartLabel(labelX, value, FormatTick(value), Palette.TextFaint, true, 9))
            .ToArray();

        ResultChart.Band = FunctionTrace.CreateVarianceTrace(Compose, DomainStart, DomainEnd);
        ResultChart.Series = trace
            .Select(segment => new ChartSeries { Points = segment, Stroke = Palette.Amber, Thickness = 2 })
            .ToArray();

        var formula = FunctionLibrary.ComposeFormula(Stages, "x");
        ResultChart.XAxisTitle = "x (normalised tank_01.level)";
        ResultChart.YAxisTitle = $"h(x) = {TrimText(formula, 40)}";
        ResultChart.Refresh();

        ResultPanel.Title = $"RESULT · h(x) = {TrimText(formula, 46)}";
        var sampleCount = trace.Sum(segment => segment.Count);
        ResultFooter.Text =
            $"trace: {sampleCount} samples · {trace.Count} continuous segment(s) · autoscaled y ∈ [{FormatTick(yMin)}, {FormatTick(yMax)}] · σ band is a placeholder";

        UpdateDraftBar();
    }

    private void UpdateDraftBar()
    {
        Draft.CommandText = $"COMPOSE {ChainText()} -> {ActiveDraft.Name} AS h";
        Draft.ChipContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Chip { Text = $"STAGES: {Stages.Count}", Accent = "cyan" },
                new Chip { Text = ActiveDraft.HasUnsavedChanges ? "UNSAVED" : "SAVED ✓", Accent = ActiveDraft.HasUnsavedChanges ? "amber" : "green" },
                new Chip { Text = "σ band: placeholder", Accent = "amber" }
            }
        };
    }

    private static string FormatTick(double value)
    {
        if (Math.Abs(value) < 1e-9) return "0";
        return Math.Abs(value) >= 10000
            ? value.ToString("0.#e+0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string TrimText(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
