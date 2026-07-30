// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

public sealed record PositionRow(string Commodity, string Quantity, string Sigma, string Delta, bool DeltaUp, double[] Trend);

public sealed record LeaderboardRow(string Rank, string Model, string Score, string Delta, int Direction, bool Dimmed);

public sealed record CalibrationPoint(double Predicted, double Observed, bool OverConfident);

public sealed record EventLogEntry(string Time, string Id, string Kind, string Detail, string Accent);

public sealed record NamedSeries(string Label, double[] Xs, double[] Ys);

public sealed record DataSnapshot(
    DateTimeOffset AsOf,
    SiteAlpha Site,
    DataTreeNode Tree,
    IReadOnlyList<PositionRow> Positions,
    IReadOnlyList<LeaderboardRow> Leaderboard,
    IReadOnlyList<CalibrationPoint> Calibration,
    IReadOnlyList<NamedSeries> WealthSeries,
    double WealthThreshold,
    IReadOnlyList<NamedSeries> SurvivalSeries,
    double[] IntensityBars,
    double[] IntensityLine,
    IReadOnlyList<EventLogEntry> Events,
    long EventCount);
