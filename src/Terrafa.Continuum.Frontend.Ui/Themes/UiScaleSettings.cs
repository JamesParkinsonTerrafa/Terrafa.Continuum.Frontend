// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// The operator's multiplier on top of the window-fit scale. 1.0 means the plate is drawn exactly
/// as the window fit computes it; above or below trades screen coverage for size, with the plate
/// pinned to the top-left so overflow is always off the bottom-right.
/// </summary>
public static class UiScaleSettings
{
    public const double MinScale = 0.7;
    public const double MaxScale = 1.3;

    public static double Scale { get; private set; } = 1.0;

    public static event Action? Changed;

    public static void SetScale(double value)
    {
        var clamped = Math.Clamp(value, MinScale, MaxScale);
        if (Math.Abs(Scale - clamped) < 0.0001) return;
        Scale = clamped;
        Changed?.Invoke();
    }
}
