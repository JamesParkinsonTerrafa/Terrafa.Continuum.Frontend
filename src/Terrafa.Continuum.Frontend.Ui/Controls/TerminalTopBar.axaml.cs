// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public partial class TerminalTopBar : UserControl
{
    public static readonly StyledProperty<string> CommandTextProperty =
        AvaloniaProperty.Register<TerminalTopBar, string>(nameof(CommandText), "");

    public static readonly StyledProperty<string> SubtitleTextProperty =
        AvaloniaProperty.Register<TerminalTopBar, string>(nameof(SubtitleText), "");

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<TerminalTopBar, object?>(nameof(RightContent));

    private readonly BubbleKeyAnimator pointerBubble;

    private bool pointersWereOnAtPress;

    public TerminalTopBar()
    {
        InitializeComponent();
        SettingsButton.PointerPressed += (_, e) =>
        {
            SettingsFlyout.RequestToggle();
            e.Handled = true;
        };
        BrandButton.PointerPressed += (_, e) =>
        {
            ContactDialog.RequestShow();
            e.Handled = true;
        };
        ModeButton.PointerPressed += (_, e) =>
        {
            BuilderModeSettings.Toggle();
            e.Handled = true;
        };
        pointerBubble = new BubbleKeyAnimator(PointerButton);

        // The animator raises PopStarted only on the way down, so that arm turns the pointers on.
        // A press on a key that is already down never reaches it, which is the arm that turns them
        // off — hence the state captured at press, before PopStarted can flip it.
        pointerBubble.PopStarted += _ => PointerHintSettings.SetEnabled(true);
        PointerButton.PointerPressed += (_, _) => pointersWereOnAtPress = PointerHintSettings.Enabled;
        PointerButton.PointerReleased += (_, e) =>
        {
            if (!pointersWereOnAtPress) return;
            if (!new Rect(PointerButton.Bounds.Size).Contains(e.GetPosition(PointerButton))) return;
            PointerHintSettings.SetEnabled(false);
        };
        RefreshModeLabels();
        RefreshPointerLabel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BuilderModeSettings.Changed += RefreshModeLabels;
        PointerHintSettings.Changed += AnimatePointerButton;
        RefreshModeLabels();
        SettlePointerButton();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        BuilderModeSettings.Changed -= RefreshModeLabels;
        PointerHintSettings.Changed -= AnimatePointerButton;
    }

    /// <summary>
    /// Each screen builds its own top bar, so a screen switch while the bubbles are up has to draw
    /// the key already popped rather than replay the animation on arrival.
    /// </summary>
    private void SettlePointerButton()
    {
        if (PointerHintSettings.Enabled) pointerBubble.RestPopped();
        else pointerBubble.RestInflated();
        RefreshPointerLabel();
    }

    private void AnimatePointerButton()
    {
        if (PointerHintSettings.Enabled)
        {
            if (!pointerBubble.IsPoppedOrPopping) pointerBubble.PopProgrammatic();
        }
        else
        {
            pointerBubble.Inflate();
        }
        RefreshPointerLabel();
    }

    private void RefreshPointerLabel()
    {
        var on = PointerHintSettings.Enabled;
        PointerLabel.Foreground = on ? Palette.Amber : Palette.TextSub;
        PointerLabel.FontWeight = on ? FontWeight.Bold : FontWeight.Normal;
    }

    private void RefreshModeLabels()
    {
        var active = BuilderModeSettings.Enabled ? TechnicalLabel : PlainLabel;
        var inactive = BuilderModeSettings.Enabled ? PlainLabel : TechnicalLabel;
        active.Foreground = Palette.Amber;
        active.FontWeight = FontWeight.Bold;
        inactive.Foreground = Palette.TextFaint;
        inactive.FontWeight = FontWeight.Normal;
    }

    public string CommandText
    {
        get => GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public string SubtitleText
    {
        get => GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }
}
