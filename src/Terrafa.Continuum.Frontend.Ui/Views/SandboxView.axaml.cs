// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

/// <summary>
/// The sandbox screen: a conversation with a Managed Agents session that has a container of its
/// own and the data tree behind a custom tool. All state lives in <see cref="SandboxAgent"/> —
/// this control is rebuilt on every theme change and navigation, and only draws what it is told.
/// </summary>
public partial class SandboxView : UserControl
{
    private TextBox? keyBox;
    private int drawnEntries;

    public SandboxView() : this(_ => { })
    {
    }

    public SandboxView(Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        Composer.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                Send();
            }
        };
        ComposerKeys.Children.Add(CommandKey("SEND", primary: true, Send));

        NoiseOverlay.Attach(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SandboxAgent.Instance.Changed += Refresh;
        SandboxAgent.Instance.Start();
        drawnEntries = 0;
        TranscriptBody.Children.Clear();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SandboxAgent.Instance.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    private void Send()
    {
        var text = Composer.Text ?? "";
        if (text.Trim().Length == 0 || !SandboxAgent.Instance.CanSend) return;
        Composer.Text = "";
        _ = SandboxAgent.Instance.SendAsync(text);
    }

    private void Refresh()
    {
        BuildPanel();
        AppendNewEntries();

        var agent = SandboxAgent.Instance;
        Composer.IsEnabled = agent.CanSend;
        StatusRight.Text = agent.Phase switch
        {
            SandboxPhase.NoKey => "NO KEY",
            SandboxPhase.Connecting => "CONNECTING…",
            SandboxPhase.Running => $"AGENT WORKING · {ShortSession(agent.SessionId)}",
            SandboxPhase.Failed => "FAILED",
            _ => agent.SessionId is null ? "READY · NO SESSION" : $"READY · {ShortSession(agent.SessionId)}"
        };
    }

    private static string ShortSession(string? sessionId) =>
        sessionId is null ? "" : sessionId.Length <= 16 ? sessionId : sessionId[..16] + "…";

    // ── side panel ───────────────────────────────────────────────────────────────────

    private void BuildPanel()
    {
        var agent = SandboxAgent.Instance;
        PanelBody.Children.Clear();
        keyBox = null;

        switch (agent.Phase)
        {
            case SandboxPhase.NoKey:
            case SandboxPhase.Failed:
                BuildKeyEntry(agent);
                break;

            case SandboxPhase.Connecting:
                PanelBody.Children.Add(SectionLabel("ANTHROPIC"));
                PanelBody.Children.Add(NoteText(agent.Note, Palette.TextMuted));
                break;

            default:
                BuildConnected(agent);
                break;
        }
    }

    private void BuildKeyEntry(SandboxAgent agent)
    {
        PanelBody.Children.Add(SectionLabel("ANTHROPIC API KEY"));
        keyBox = new TextBox
        {
            Classes = { "field" },
            PasswordChar = '•',
            Watermark = "sk-ant-…"
        };
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                Connect();
            }
        };
        PanelBody.Children.Add(keyBox);
        PanelBody.Children.Add(CommandKey("CONNECT", primary: true, Connect));

        if (agent.Note.Length > 0)
        {
            PanelBody.Children.Add(NoteText(agent.Note,
                agent.Phase == SandboxPhase.Failed ? Palette.Red : Palette.Amber));
        }

        PanelBody.Children.Add(NoteText(
            "the key is yours: it stays on this device and is spent against your own anthropic account",
            Palette.TextFaint));

        PanelBody.Children.Add(SectionLabel("NO KEY?"));
        PanelBody.Children.Add(CommandKey("ASK TERRAFA FOR ONE", primary: false, ContactDialog.RequestShow));
    }

    private void BuildConnected(SandboxAgent agent)
    {
        PanelBody.Children.Add(SectionLabel("STATUS"));
        PanelBody.Children.Add(NoteText(
            agent.Phase == SandboxPhase.Running ? "agent working in its container" : "connected and idle",
            Palette.TextSub));
        if (agent.Note.Length > 0) PanelBody.Children.Add(NoteText(agent.Note, Palette.TextFaint));

        PanelBody.Children.Add(SectionLabel("SESSION"));
        PanelBody.Children.Add(NoteText(
            agent.SessionId is null ? "none yet — the first message starts one" : agent.SessionId,
            Palette.TextFaint));
        PanelBody.Children.Add(CommandKey("NEW SESSION", primary: false, SandboxAgent.Instance.NewSession));

        PanelBody.Children.Add(SectionLabel("KEY"));
        PanelBody.Children.Add(CommandKey("DISCONNECT + FORGET KEY", primary: false,
            () => _ = SandboxAgent.Instance.DisconnectAsync()));
    }

    private void Connect()
    {
        var key = keyBox?.Text ?? "";
        if (key.Trim().Length == 0) return;
        _ = SandboxAgent.Instance.ConnectAsync(key);
    }

    // ── transcript ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends only what is new. The transcript is append-only in the service, so redrawing the
    /// whole conversation on every activity line would be pure churn — and would throw away text
    /// selection mid-read.
    /// </summary>
    private void AppendNewEntries()
    {
        var entries = SandboxAgent.Instance.Transcript;
        if (entries.Count < drawnEntries)
        {
            TranscriptBody.Children.Clear();
            drawnEntries = 0;
        }
        if (entries.Count == drawnEntries) return;

        for (var i = drawnEntries; i < entries.Count; i++)
        {
            TranscriptBody.Children.Add(Render(entries[i]));
        }
        drawnEntries = entries.Count;
        Dispatcher.UIThread.Post(TranscriptScroll.ScrollToEnd, DispatcherPriority.Loaded);
    }

    private static Control Render(SandboxEntry entry) => entry.Kind switch
    {
        SandboxEntryKind.User => Labelled("YOU", Palette.Amber, entry.Text, Palette.TextBright),
        SandboxEntryKind.Agent => Labelled("AGENT", Palette.Cyan, entry.Text, Palette.Text),
        SandboxEntryKind.Error => Labelled("ERROR", Palette.Red, entry.Text, Palette.Red),
        _ => new TextBlock
        {
            Text = $"· {entry.Text}",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static Control Labelled(string label, IBrush labelBrush, string text, IBrush textBrush)
    {
        var block = new StackPanel { Spacing = 3 };
        block.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Foreground = labelBrush
        });
        block.Children.Add(new SelectableTextBlock
        {
            Text = text,
            FontSize = TypographySettings.Size(11),
            LineHeight = TypographySettings.Size(16),
            Foreground = textBrush,
            TextWrapping = TextWrapping.Wrap
        });
        return block;
    }

    // ── shared key/label helpers, in this screen's own dialect of the house style ────

    private static TextBlock SectionLabel(string label) => new()
    {
        Text = label,
        FontSize = TypographySettings.Size(9),
        LetterSpacing = 1,
        Foreground = Palette.TextFaint,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private static TextBlock NoteText(string text, IBrush brush) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = TypographySettings.Size(9),
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap
    };

    private static Control CommandKey(string label, bool primary, Action action)
    {
        var key = new SquircleBorder
        {
            Classes = { primary ? "emboss-key" : "emboss" },
            Padding = new Thickness(14, 6),
            Background = primary ? Palette.Amber : Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = label,
                FontSize = TypographySettings.Size(10),
                LetterSpacing = 1,
                FontWeight = primary ? FontWeight.Bold : FontWeight.Normal,
                Foreground = primary ? Brushes.Black : Palette.TextSub
            }
        };
        key.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return key;
    }
}
