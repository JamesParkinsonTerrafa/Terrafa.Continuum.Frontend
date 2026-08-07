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
    /// DASHBOARD, MAP, CSV EXPORT, SANDBOX — the values are screen indices in
    /// TerminalTabStrip.NavigationLabels order.
    /// </summary>
    public static readonly IReadOnlyList<int> Default = [5, 3, 1, 0, 2, 4, 6, 7];

    private static int[] order = [.. Default];

    public static event Action? Changed;

    /// <summary>The screen index shown at each display position.</summary>
    public static IReadOnlyList<int> OrderFor(int count)
    {
        if (order.Length != count) order = [.. Enumerable.Range(0, count)];
        return order;
    }

    /// <summary>
    /// Accepts any distinct in-range subset and appends whatever screens it omits, so an order
    /// persisted before a screen existed migrates to "your order, new screens at the end" instead
    /// of silently resetting when <see cref="OrderFor"/> sees the wrong length. Garbage entries
    /// are dropped; an order with nothing valid is ignored.
    /// </summary>
    public static void Set(IReadOnlyList<int> newOrder)
    {
        var sanitized = newOrder
            .Where(index => index >= 0 && index < Default.Count)
            .Distinct()
            .ToList();
        if (sanitized.Count == 0) return;
        sanitized.AddRange(Enumerable.Range(0, Default.Count).Where(index => !sanitized.Contains(index)));

        if (order.SequenceEqual(sanitized)) return;
        order = [.. sanitized];
        Changed?.Invoke();
    }
}
