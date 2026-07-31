// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// The placement grid behind the free canvases — the dashboard's tiles and the model network's
/// node cards — on by default, drawn as faint gridlines.
///
/// While a box is being dragged it only *leans* toward the nearest gridline — the magnetic feel —
/// and the hard lock happens on release. Turning the setting on snaps everything already placed
/// to its nearest gridline, so a canvas laid out free does not stay half-aligned forever.
/// </summary>
public static class SnapSettings
{
    /// <summary>Gridline spacing in canvas pixels. The seeded board is laid out in multiples of it.</summary>
    public const double GridSize = 25;

    /// <summary>How close to a gridline the magnetic pull starts — under half a cell, or every
    /// point on the canvas would be inside some line's pull and the whole drag would feel mushy.</summary>
    private const double MagnetRange = 8;

    /// <summary>How much of the remaining offset survives the pull — 0 is a hard lock, 1 is none.</summary>
    private const double MagnetGive = 0.3;

    public static bool Enabled { get; private set; } = true;

    /// <summary>Whether the canvas draws the faint gridlines — visual only, snapping is unaffected.</summary>
    public static bool ShowGridLines { get; private set; } = true;

    public static event Action? Changed;

    public static void Toggle() => SetEnabled(!Enabled);

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        Changed?.Invoke();
    }

    public static void ToggleGridLines() => SetShowGridLines(!ShowGridLines);

    public static void SetShowGridLines(bool show)
    {
        if (ShowGridLines == show) return;
        ShowGridLines = show;
        Changed?.Invoke();
    }

    /// <summary>The nearest gridline.</summary>
    public static double Snap(double value) => Math.Round(value / GridSize) * GridSize;

    /// <summary>The nearest gridline that keeps the value at or above <paramref name="minimum"/>.</summary>
    public static double SnapAtLeast(double value, double minimum)
    {
        var snapped = Snap(value);
        while (snapped < minimum) snapped += GridSize;
        return snapped;
    }

    /// <summary>The magnetic pull while dragging: near a gridline the value gives toward it.</summary>
    public static double Magnetize(double value)
    {
        var line = Snap(value);
        var offset = value - line;
        return Math.Abs(offset) <= MagnetRange ? line + offset * MagnetGive : value;
    }
}
