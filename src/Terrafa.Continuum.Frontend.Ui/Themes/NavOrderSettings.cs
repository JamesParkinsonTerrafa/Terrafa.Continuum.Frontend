// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// The operator's ordering of the navigation tabs. Every screen hosts its own copy of the tab
/// strip, so the order lives here rather than in any one strip; a drag on one screen's strip is
/// what every screen shows afterwards. The stored values are screen indices — the labels'
/// number prefixes are cosmetic and follow display position, not screen identity.
/// </summary>
public static class NavOrderSettings
{
    /// <summary>
    /// Data flows left to right by default: DATA SOURCES, DATA TREE, TRANSFER FUNCTION, NETWORK,
    /// DASHBOARD, MAP — the values are screen indices in TerminalTabStrip.NavigationLabels order.
    /// </summary>
    public static readonly IReadOnlyList<int> Default = [5, 3, 1, 0, 2, 4];

    private static int[] order = [.. Default];

    public static event Action? Changed;

    /// <summary>The screen index shown at each display position.</summary>
    public static IReadOnlyList<int> OrderFor(int count)
    {
        if (order.Length != count) order = [.. Enumerable.Range(0, count)];
        return order;
    }

    public static void Set(IReadOnlyList<int> newOrder)
    {
        if (order.SequenceEqual(newOrder)) return;
        order = [.. newOrder];
        Changed?.Invoke();
    }
}
