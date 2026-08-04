// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

    private sealed class FunctionDraft
    {
        public required string Name { get; set; }
        public CompositionNode Root { get; set; } = new VariableNode();
        public string? SavedName { get; set; }
        public bool HasUnsavedChanges { get; set; }
    }

    private sealed record TreeRow(Panel Row, CompositionNode Node, FunctionNode? Parent, int Index);

    private readonly FunctionLibrary library = FunctionLibrary.Instance;
    private readonly Action<int> navigate;
    private readonly List<FunctionDraft> drafts = [];
    private readonly List<TreeRow> treeRows = [];
    private readonly List<(FunctionNode Node, NodeCard Card)> functionCards = [];
    private NodeCard? outputCard;
    private Panel? outputRow;
    private int activeDraftIndex;
    private bool isSyncingNameBox;

    private readonly HashSet<string> collapsedGroups = [.. FunctionLibrary.PlannedGroups];
    private LibraryFunction? pressedFunction;
    private Point pressOrigin;
    private bool libraryDragActive;
    private Border? dragGhost;
    private Border? openMenu;

    internal IReadOnlyList<Panel> NodeRows => treeRows.Select(entry => entry.Row).ToArray();

    internal string RootFormula => ActiveDraft.Root.Formula("x");

    private FunctionDraft ActiveDraft => drafts[activeDraftIndex];

    public TransferFunctionView() : this(_ => { })
    {
    }

    public TransferFunctionView(Action<int> navigate)
    {
        InitializeComponent();
        this.navigate = navigate;
        Tabs.TabSelected += navigate;

        var initial = new FunctionDraft { Name = "fig.draft_h" };
        var square = library.Find("square")!;
        var reciprocal = library.Find("reciprocal")!;
        initial.Root = new FunctionNode(reciprocal, [new FunctionNode(square, [new VariableNode()])]);
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
        HintSettings.Changed += RebuildTree;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HintSettings.Changed -= RebuildTree;
    }

    private void RebuildLibrary()
    {
        LibraryList.Children.Clear();
        foreach (var group in FunctionLibrary.PrimitiveGroups)
            AddLibraryGroup(group, library.PrimitivesInGroup(group));
        foreach (var group in FunctionLibrary.EstimatorGroups)
            AddEstimatorGroup(group, library.EstimatorsInGroup(group));
        foreach (var group in FunctionLibrary.PlannedGroups)
            AddLibraryGroup(group, []);
        if (library.UserFunctions.Count > 0)
            AddLibraryGroup(FunctionLibrary.CompositesGroup, library.UserFunctions);
    }

    private void AddEstimatorGroup(string group, IReadOnlyList<FunctionEstimator> estimators)
    {
        var collapsed = collapsedGroups.Contains(group);
        LibraryList.Children.Add(CreateGroupHeader(group, estimators.Count, collapsed));
        if (collapsed) return;
        foreach (var estimator in estimators)
            LibraryList.Children.Add(CreateEstimatorEntry(estimator));
    }

    private void AddLibraryGroup(string group, IReadOnlyList<LibraryFunction> functions)
    {
        var collapsed = collapsedGroups.Contains(group);
        LibraryList.Children.Add(CreateGroupHeader(group, functions.Count, collapsed));
        if (collapsed) return;

        if (functions.Count == 0)
        {
            LibraryList.Children.Add(new TextBlock
            {
                Text = "no functions yet",
                FontSize = TypographySettings.Size(10),
                Margin = new Thickness(17, 0, 0, 0),
                Foreground = Palette.TextGhost
            });
            return;
        }

        foreach (var function in functions)
            LibraryList.Children.Add(CreateLibraryEntry(function));
    }

    private Control CreateGroupHeader(string group, int count, bool collapsed)
    {
        var caret = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = group,
            FontSize = TypographySettings.Size(10),
            LetterSpacing = 1,
            Foreground = Palette.TextSub,
            VerticalAlignment = VerticalAlignment.Center
        };
        var countBlock = new TextBlock
        {
            Text = $"({count})",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        row.Children.Add(caret);
        row.Children.Add(label);
        row.Children.Add(countBlock);

        var shell = new Border
        {
            Padding = new Thickness(2, 3),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (!collapsedGroups.Remove(group)) collapsedGroups.Add(group);
            RebuildLibrary();
        };
        return shell;
    }

    private Border CreateLibraryEntry(LibraryFunction function)
    {
        var tag = new TextBlock
        {
            Classes = { "tag" },
            Text = function.IsPrimitive ? $"PRIMITIVE · {function.ArityLabel}" : $"COMPOSITE · {function.ArityLabel}",
            Foreground = function.IsPrimitive ? Palette.TextMuted : Palette.Purple
        };
        var signature = new TextBlock
        {
            Classes = { "tag" },
            Text = function.SignatureText,
            Foreground = Palette.TextFaint
        };
        var tagRow = new DockPanel();
        DockPanel.SetDock(signature, Dock.Right);
        tagRow.Children.Add(signature);
        tagRow.Children.Add(tag);

        var formula = new TextBlock
        {
            Text = $"{function.Name}: {function.DisplayFormula}",
            FontSize = TypographySettings.Size(11),
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = function.IsPrimitive ? Palette.TextMuted : Palette.PurpleSoft
        };
        var body = new StackPanel { Children = { tagRow, formula } };
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
                ("APPLY TO OUTPUT", () => WrapNode(null, 0, function)),
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
        var target = RowAt(e.GetPosition(StackHost));
        if (target is null)
            WrapNode(null, 0, function);
        else
            WrapNode(target.Parent, target.Index, function);
    }

    private Border CreateEstimatorEntry(FunctionEstimator estimator)
    {
        var tag = new TextBlock
        {
            Classes = { "tag" },
            Text = $"ESTIMATOR · {estimator.ArityLabel}",
            Foreground = Palette.Cyan
        };
        var signature = new TextBlock
        {
            Classes = { "tag" },
            Text = estimator.SignatureText,
            Foreground = Palette.TextFaint
        };
        var tagRow = new DockPanel();
        DockPanel.SetDock(signature, Dock.Right);
        tagRow.Children.Add(signature);
        tagRow.Children.Add(tag);

        var formula = new TextBlock
        {
            Text = $"{estimator.Name}: {estimator.DisplayFormula}",
            FontSize = TypographySettings.Size(11),
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Palette.CyanSoft
        };
        var body = new StackPanel { Children = { tagRow, formula } };
        body.Children.Add(new TextBlock
        {
            Classes = { "tag" },
            Text = estimator.Note,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap
        });

        var entry = new Border
        {
            BorderBrush = Palette.Cyan,
            BorderThickness = new Thickness(1),
            Background = Palette.CyanFill,
            Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body
        };
        entry.PointerEntered += (_, _) => entry.Background = Palette.BgField;
        entry.PointerExited += (_, _) => entry.Background = Palette.CyanFill;
        entry.PointerPressed += (_, e) => OnEstimatorEntryPressed(estimator, e);
        return entry;
    }

    private void OnEstimatorEntryPressed(FunctionEstimator estimator, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) return;
        e.Handled = true;
        ShowMenu($"{estimator.Name} {estimator.SignatureText}", e.GetPosition(this),
            ("USE ON NETWORK — WIRE x[], y[] + PREDICT", () => navigate(0)));
    }

    private TreeRow? RowAt(Point point)
    {
        TreeRow? nearest = null;
        var nearestDistance = double.MaxValue;
        foreach (var entry in treeRows)
        {
            if (entry.Row.Bounds.Contains(point)) return entry;
            var distance = Math.Abs(entry.Row.Bounds.Center.Y - point.Y);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = entry;
            }
        }
        if (outputRow is not null && point.Y <= outputRow.Bounds.Bottom) return null;
        return nearest;
    }

    private CompositionNode NodeAt(FunctionNode? parent, int index) =>
        parent is null ? ActiveDraft.Root : parent.Arguments[index];

    private void WrapNode(FunctionNode? parent, int index, LibraryFunction function) =>
        ReplaceNode(parent, index, FunctionNode.Create(function, NodeAt(parent, index)));

    private void ReplaceNode(FunctionNode? parent, int index, CompositionNode replacement)
    {
        if (parent is null)
            ActiveDraft.Root = replacement;
        else
            parent.Arguments[index] = replacement;
        OnTreeEdited();
    }

    private void OnTreeEdited()
    {
        ActiveDraft.HasUnsavedChanges = true;
        RebuildTabs();
        RebuildTree();
        RefreshResult();
    }

    private void RebuildTree()
    {
        StackHost.Children.Clear();
        treeRows.Clear();
        functionCards.Clear();

        outputCard = new NodeCard
        {
            Variant = NodeCardVariant.Figure,
            TagText = "OUTPUT · h",
            TagRight = "x → h(x)",
            Title = $"h(x) = {TrimText(RootFormula, 44)}",
            TitleSize = 15,
            Margin = new Thickness(0, 0, 0, 10)
        };
        outputRow = new Panel { Background = Brushes.Transparent };
        outputRow.Children.Add(outputCard);
        outputRow.PointerPressed += (_, e) => OnOutputPressed(e);
        StackHost.Children.Add(outputRow);

        AppendRows(ActiveDraft.Root, null, 0, "", true);

        StackFooter.Text = FooterText();
    }

    private string FooterText()
    {
        var formula = TrimText(RootFormula, 58);
        if (ActiveDraft.Root.CountFunctionNodes() == 0)
        {
            return HintSettings.Enabled
                ? "tree is empty — drag a library function onto the input, or right-click it · h(x) = x"
                : "tree is empty · h(x) = x";
        }
        return HintSettings.Enabled
            ? $"h(x) = {formula} · each argument branches below its function · drop wraps the node it lands on"
            : $"h(x) = {formula}";
    }

    private void OnOutputPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        ShowMenu("OUTPUT · h", e.GetPosition(this),
            ("CLEAR — h(x) = x", () => ReplaceNode(null, 0, new VariableNode())));
    }

    private void AppendRows(CompositionNode node, FunctionNode? parent, int index, string ancestorGuides, bool isLast)
    {
        var row = CreateTreeRow(node, parent, index, ancestorGuides, isLast);
        treeRows.Add(new TreeRow(row, node, parent, index));
        StackHost.Children.Add(row);

        if (node is not FunctionNode functionNode) return;
        var childGuides = parent is null ? "" : ancestorGuides + (isLast ? "    " : "│   ");
        for (var childIndex = 0; childIndex < functionNode.Arguments.Count; childIndex++)
        {
            AppendRows(functionNode.Arguments[childIndex], functionNode, childIndex, childGuides,
                childIndex == functionNode.Arguments.Count - 1);
        }
    }

    private Panel CreateTreeRow(CompositionNode node, FunctionNode? parent, int index, string ancestorGuides, bool isLast)
    {
        var content = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (parent is not null)
        {
            var prefix = new TextBlock
            {
                FontSize = TypographySettings.Size(11),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            prefix.Inlines =
            [
                new Run(ancestorGuides + (isLast ? "└─ " : "├─ ")) { Foreground = Palette.TextGhost },
                new Run(parent.PortLabel(index)) { Foreground = Palette.TextFaint }
            ];
            DockPanel.SetDock(prefix, Dock.Left);
            content.Children.Add(prefix);
        }
        content.Children.Add(CreateNodeCard(node, parent, index));

        var row = new Panel { Background = Brushes.Transparent };
        row.Children.Add(content);
        row.PointerPressed += (_, e) => OnRowPressed(node, parent, index, e);
        return row;
    }

    private Control CreateNodeCard(CompositionNode node, FunctionNode? parent, int index) => node switch
    {
        FunctionNode functionNode => CreateFunctionCard(functionNode, parent, index),
        ConstantNode constantNode => CreateConstantCard(constantNode),
        _ => new NodeCard
        {
            Variant = NodeCardVariant.Measure,
            TagText = "INPUT",
            TagRight = "x",
            Title = "x · tank_01.level (norm.)",
            TitleSize = 12
        }
    };

    private Control CreateFunctionCard(FunctionNode node, FunctionNode? parent, int index)
    {
        var card = new NodeCard
        {
            Variant = node.Function.IsPrimitive ? NodeCardVariant.Transfer : NodeCardVariant.Provisional,
            TagText = $"fn · {node.Function.Name} · {node.Function.ArityLabel}",
            TagRight = node.Function.SignatureText,
            Title = TrimText(node.Formula("x"), 38),
            TitleSize = 13
        };
        functionCards.Add((node, card));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        if (node.CanAddArgument)
        {
            buttons.Children.Add(CreateRowButton("+", Palette.Green, () =>
            {
                node.Arguments.Add(new VariableNode());
                OnTreeEdited();
            }));
        }
        buttons.Children.Add(CreateRowButton("✕", Palette.Red, () => ReplaceNode(parent, index, node.Arguments[0])));

        var host = new Panel();
        host.Children.Add(card);
        host.Children.Add(buttons);
        return host;
    }

    private Control CreateRowButton(string glyph, IBrush hoverBrush, Action action)
    {
        var text = new TextBlock { Text = glyph, FontSize = TypographySettings.Size(10), Foreground = Palette.TextFaint };
        var button = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 3),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text
        };
        button.PointerEntered += (_, _) => text.Foreground = hoverBrush;
        button.PointerExited += (_, _) => text.Foreground = Palette.TextFaint;
        button.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private Control CreateConstantCard(ConstantNode node)
    {
        var valueBox = new TextBox
        {
            Classes = { "field" },
            Width = 90,
            Text = ConstantNode.Format(node.Value),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        valueBox.TextChanged += (_, _) =>
        {
            node.Value = double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : double.NaN;
            ActiveDraft.HasUnsavedChanges = true;
            RebuildTabs();
            UpdateFormulaTexts();
            RefreshResult();
        };
        return new NodeCard
        {
            Variant = NodeCardVariant.ObjectNode,
            TagText = "CONST",
            TagRight = "c",
            ExtraContent = valueBox
        };
    }

    private void UpdateFormulaTexts()
    {
        if (outputCard is not null)
            outputCard.Title = $"h(x) = {TrimText(RootFormula, 44)}";
        foreach (var (node, card) in functionCards)
            card.Title = TrimText(node.Formula("x"), 38);
        StackFooter.Text = FooterText();
    }

    private void OnRowPressed(CompositionNode node, FunctionNode? parent, int index, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        var items = new List<(string Label, Action Action)>();

        var canDropSlot = parent is not null && parent.CanRemoveArgument;
        var removeAction = canDropSlot
            ? () =>
            {
                parent!.Arguments.RemoveAt(index);
                OnTreeEdited();
            }
            : (Action)(() => ReplaceNode(parent, index, new VariableNode()));
        if (node is not VariableNode || canDropSlot)
            items.Add(("REMOVE", removeAction));

        switch (node)
        {
            case FunctionNode functionNode:
                items.Add(($"UNWRAP — LIFT {functionNode.PortLabel(0)}",
                    () => ReplaceNode(parent, index, functionNode.Arguments[0])));
                if (functionNode.CanAddArgument)
                {
                    items.Add(("ADD ARGUMENT", () =>
                    {
                        functionNode.Arguments.Add(new VariableNode());
                        OnTreeEdited();
                    }));
                }
                break;
            case ConstantNode:
                items.Add(("SET TO x", () => ReplaceNode(parent, index, new VariableNode())));
                break;
            default:
                items.Add(("SET CONSTANT", () => ReplaceNode(parent, index, new ConstantNode(1.0))));
                break;
        }

        if (parent is not null)
        {
            if (index > 0)
                items.Add(("MOVE UP", () => SwapArguments(parent, index, index - 1)));
            if (index < parent.Arguments.Count - 1)
                items.Add(("MOVE DOWN", () => SwapArguments(parent, index, index + 1)));
        }

        ShowMenu(DescribeNode(node), e.GetPosition(this), items.ToArray());
    }

    private void SwapArguments(FunctionNode parent, int first, int second)
    {
        (parent.Arguments[first], parent.Arguments[second]) = (parent.Arguments[second], parent.Arguments[first]);
        OnTreeEdited();
    }

    private static string DescribeNode(CompositionNode node) => node switch
    {
        FunctionNode functionNode => $"fn · {functionNode.Function.Name}",
        ConstantNode constantNode => $"const · {ConstantNode.Format(constantNode.Value)}",
        _ => "input · x"
    };

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
                FontSize = TypographySettings.Size(9),
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
            FontSize = TypographySettings.Size(10),
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
                        FontSize = TypographySettings.Size(13),
                        LineHeight = TypographySettings.Size(19),
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
            FontSize = TypographySettings.Size(10),
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
        RebuildTree();
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
        if (draft.Root.CountFunctionNodes() == 0)
        {
            SetStatus($"{name} is bare x — compose at least one function before saving", true);
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
        library.SaveComposite(name, draft.Root);
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
                Text = $"{function.Name} {function.SignatureText}",
                FontSize = TypographySettings.Size(11),
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
        var root = ActiveDraft.Root;
        double Compose(double x) => root.Evaluate(x);

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

        var formula = RootFormula;
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
        Draft.CommandText = $"COMPOSE h(x) = {TrimText(RootFormula, 42)} -> {ActiveDraft.Name}";
        Draft.ChipContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Chip { Text = $"NODES: {ActiveDraft.Root.CountFunctionNodes()}", Accent = "cyan" },
                new Chip { Text = $"DEPTH: {ActiveDraft.Root.Depth()}", Accent = "cyan" },
                new Chip { Text = ActiveDraft.HasUnsavedChanges ? "UNSAVED" : "SAVED ✓", Accent = ActiveDraft.HasUnsavedChanges ? "amber" : "green" },
                new Chip { Text = "σ band: placeholder", Accent = "amber" }
            }
        };
    }

    private static string FormatTick(double value)
    {
        if (Math.Abs(value) < 1e-9) return "0";
        return Math.Abs(value) >= 10000
            ? value.ToString("0.###e+0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string TrimText(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
