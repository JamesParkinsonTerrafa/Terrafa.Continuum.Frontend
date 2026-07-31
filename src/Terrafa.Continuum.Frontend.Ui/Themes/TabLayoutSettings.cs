// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

public static class TabLayoutSettings
{
    public static bool Vertical { get; private set; } = true;

    public static event Action? Changed;

    public static void Toggle() => SetVertical(!Vertical);

    public static void SetVertical(bool vertical)
    {
        if (Vertical == vertical) return;
        Vertical = vertical;
        Changed?.Invoke();
    }
}
