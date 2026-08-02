// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

public static class PointerHintSettings
{
    private static readonly HashSet<int> visitedScreens = [];
    private static readonly HashSet<(int Screen, string Target)> dismissedHints = [];

    public static bool Enabled { get; private set; }

    public static bool AutoShow { get; set; } = true;

    public static event Action? Changed;

    /// <summary>
    /// Turning the pointers on brings back everything closed since they were last up. Otherwise the
    /// button would go down on a screen whose tips had all been dismissed and show nothing.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (enabled) dismissedHints.Clear();
        Changed?.Invoke();
    }

    public static bool IsDismissed(int screenIndex, string targetName) =>
        dismissedHints.Contains((screenIndex, targetName));

    public static void Dismiss(int screenIndex, string targetName)
    {
        if (!dismissedHints.Add((screenIndex, targetName))) return;
        Changed?.Invoke();
    }

    public static bool MarkVisited(int screenIndex) => visitedScreens.Add(screenIndex);

    public static bool HasVisited(int screenIndex) => visitedScreens.Contains(screenIndex);

    public static void ResetForTests()
    {
        visitedScreens.Clear();
        dismissedHints.Clear();
        Enabled = false;
        AutoShow = true;
        Changed = null;
    }
}
