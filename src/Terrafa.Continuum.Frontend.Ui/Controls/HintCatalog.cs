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
    public const int CsvExportScreen = 6;

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
            new Point(0, 170)),

            //TO POINT TO CORRECT PAGE/ LOCATION
        new HintPointer(
            "DataSources",
            HintSide.Right,
            "CENTRALISE EVERYTHING",
            "The catalogue of everything the operation could know: " 
            + "its own sites, market prices, weather, freight, policy, contracts. "
            + "This is how the picture grows. A bigger tree, and every mount makes the figures downstream sharper.",
            new Point(0, 170)),

        new HintPointer(
            "DataTree",
            HintSide.Right,
            "THE SHAPE OF THE OPERATION",
            "Beside it the event log. Every reading, every change, every new tank is a line in a list that only grows. "
            + "Value: nothing is ever overwritten, so you can wind back to any moment and see exactly what was known then. That is what makes it possible to test honestly whether we would have been right.",
            new Point(0, 170)),

        new HintPointer(
            "TransferFunction",
            HintSide.Right,
            "BUILD CONFIDENCE",
            "You focus on the analysis, the platform tells you whether it is safe. "
            + "It shows the domain, flags the pole, and draws the band with the line. "
            + "Value: this is exactly where a spreadsheet lies to you. Divide by something near zero and Excel hands you a number.",
            new Point(0, 170)),

        new HintPointer(
            "Network",
            HintSide.Right,
            "PROPAGATE CONFIDENCE",
            "Wire numbers together and the band is carried for you. Two tank levels into a total, 24,085 barrels plus or minus 152.  "
            + "On the hazard branch it says linearisation refused and switches itself to Monte Carlo, then draws the expiry risk dashed "
            + "because the frailty term is not identifiable from those leaves."
            + "Value: the whole product in one screen. It computes, it says how sure it is, and it declines when it cannot support the answer.",
            new Point(0, 170)),

        new HintPointer(
            "DashBoard",
            HintSide.Right,
            "EVERYDAY SCREEN",
            "the change here is the most important one in the build. Variance is on by default. Every chart form carries its bounds "
            + "and a tile wired to a source with no sigma is left blank rather than drawn as if it were certain. "
            + "Value:  A number that cannot show how sure it is does not get drawn.",
            new Point(0, 170)),

        new HintPointer(
            "Map",
            HintSide.Right,
            "CONTEXTUALISING YOUR DATA",
            "The number standing where the thing is. The meter's flow is drawn as an error ellipse whose long axis is the direction you are least sure about. "
            + "Value: it makes the model legible to someone who will never open the network canvas. The ellipse says the thing a single number cannot.",
            new Point(0, 170)),

        new HintPointer(
            "CSVExport",
            HintSide.Right,
            "NOTHING IS ASSUMED",
            "Every input to every step is picked by hand and stays on show. Change what feeds a "
            + "number and you can see exactly what moved, and why.",
            new Point(0, 170)),
    ];

    public static IReadOnlyList<HintPointer> For(int screenIndex) => screenIndex switch
    {
        TransferFunctionScreen => TransferFunctionHints,
        _ => []
    };
}
