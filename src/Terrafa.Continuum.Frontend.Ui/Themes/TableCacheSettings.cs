// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

public static class TableCacheSettings
{
    public const int MinCacheRows = 20_000;
    public const int MaxCacheRows = 500_000;
    public const int MinEvictionRows = 5_000;
    public const int MaxEvictionRows = 250_000;

    public static int CacheRows { get; private set; } = 100_000;

    public static int EvictionRows { get; private set; } = 25_000;

    public static event Action? Changed;

    public static void SetCacheRows(int rows)
    {
        var clamped = Math.Clamp(rows, MinCacheRows, MaxCacheRows);
        if (CacheRows == clamped) return;
        CacheRows = clamped;
        Changed?.Invoke();
    }

    public static void SetEvictionRows(int rows)
    {
        var clamped = Math.Clamp(rows, MinEvictionRows, MaxEvictionRows);
        if (EvictionRows == clamped) return;
        EvictionRows = clamped;
        Changed?.Invoke();
    }
}
