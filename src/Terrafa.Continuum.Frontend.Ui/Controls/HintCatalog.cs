// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;

namespace Terrafa.Continuum.Frontend.Controls;

public enum HintSide
{
    Left,
    Right,
    Above,
    Below
}

public sealed record HintPointer(
    string TargetName,
    HintSide Side,
    string Title,
    string Body,
    Point Nudge = default);

public static class HintCatalog
{
    public const int NetworkScreen = 0;
    public const int TransferFunctionScreen = 1;
    public const int DashboardScreen = 2;
    public const int DataTreeScreen = 3;
    public const int MapScreen = 4;
    public const int DataSourcesScreen = 5;

    private static readonly IReadOnlyList<HintPointer> TransferFunctionHints =
    [
        new HintPointer(
            "ResultChart",
            HintSide.Left,
            "EVERY NUMBER CARRIES ITS DOUBT",
            "The band is how wrong this could be. It is measured from the instruments, never "
            + "guessed, and it follows the number everywhere it goes. No decision here rests on "
            + "false precision.",
            new Point(0, 40)),
        new HintPointer(
            "StackHost",
            HintSide.Right,
            "BUILD ONCE, REUSE EVERYWHERE",
            "Stack proven steps into whatever your business actually measures. Save it and it "
            + "becomes a block the next model builds on. The logic lives here, not in someone's "
            + "spreadsheet.",
            new Point(0, -140)),
        new HintPointer(
            "LibraryList",
            HintSide.Right,
            "NOTHING IS ASSUMED",
            "Every input to every step is picked by hand and stays on show. Change what feeds a "
            + "number and you can see exactly what moved, and why.",
            new Point(0, 170))
    ];

    public static IReadOnlyList<HintPointer> For(int screenIndex) => screenIndex switch
    {
        TransferFunctionScreen => TransferFunctionHints,
        _ => []
    };
}
