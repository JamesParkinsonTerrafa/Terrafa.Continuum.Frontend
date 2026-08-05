// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// The plate-wide loading screen, held over the app from launch until the session has settled
/// into whatever it is going to show.
///
/// <para>
/// It is the twin of the web head's boot splash in <c>index.html</c> — same plate colour, same
/// line, same fade — and deliberately so: the page tears its own splash down on the first frame
/// Avalonia paints, and this is what it hands over to. Without it the operator watched the app
/// paint the demo seed and then swap in their own workspace once the stored token landed.
/// </para>
/// </summary>
public class BootOverlay : Panel
{
    /// <summary>The web splash's fade, to the millisecond, so the handover has no seam in it.</summary>
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(240);

    private readonly DispatcherTimer retire;

    public BootOverlay()
    {
        Background = Palette.BgDeep;
        IsVisible = false;
        Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = Fade }];

        Children.Add(new TextBlock
        {
            Text = "LOADING CONTINUUM CORE",
            FontFamily = Palette.Font,
            FontSize = TypographySettings.Size(11),
            FontWeight = FontWeight.Medium,
            LetterSpacing = 2,
            Foreground = Palette.TextMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        // The fade is a transition, not an animation with a completion callback, so the control is
        // taken out of the tree on a timer of the same length. Hiding it on the spot would cut the
        // fade off at its first frame.
        retire = new DispatcherTimer { Interval = Fade };
        retire.Tick += (_, _) =>
        {
            retire.Stop();
            if (Opacity == 0) IsVisible = false;
        };
    }

    /// <summary>
    /// Covers the app for as long as the session cannot say what it is showing — starting, and
    /// then loading an account's documents. Ready, failed and signed out are all answers, and the
    /// screens are the ones that report them.
    /// </summary>
    public void Follow(SessionPhase phase) =>
        Show(phase is SessionPhase.Starting or SessionPhase.Loading);

    private void Show(bool cover)
    {
        if (cover)
        {
            retire.Stop();
            Opacity = 1;
            IsVisible = true;
            IsHitTestVisible = true;
            return;
        }

        if (!IsVisible || Opacity == 0) return;
        // Stops swallowing clicks the moment it starts fading, so the app is live as it appears
        // rather than a quarter of a second after.
        IsHitTestVisible = false;
        Opacity = 0;
        retire.Start();
    }
}
