// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// The dashboard's master variance switch, on by default.
///
/// Off is the prototyping mode: every tile drops its bounds and draws the central estimate alone,
/// so a dashboard can be laid out before anything is wired up. On is the honest mode — a tile whose
/// source carries no σ blanks rather than drawing a bare line that would read as certain.
/// </summary>
public static class VarianceSettings
{
    public static bool Enabled { get; private set; } = true;

    public static event Action? Changed;

    public static void Toggle() => SetEnabled(!Enabled);

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        Changed?.Invoke();
    }
}
