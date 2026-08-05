// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// One stop on the guided tour: the screen it is shown over, what it says, and the label on the
/// key that moves on. The tour is the way in — the pointer tips on each screen are the detail, and
/// the last stop hands over to them.
/// </summary>
public sealed record TourStop(int Screen, string Title, string Body, string ActionLabel);

public static class TourCatalog
{
    /// <summary>
    /// The route, in order. The tour opens on the first stop's screen, and each key press moves to
    /// the next one — so the order here is the order the screens are visited in.
    /// </summary>
    public static readonly IReadOnlyList<TourStop> Route =
    [
        new TourStop(
            HintCatalog.MapScreen,
            "SEE WHERE THE NUMBERS CAME FROM",
            "Every figure here stands where the thing it measures stands. Follow one back and you "
            + "find the reading it came from, the model that carried it, and how sure the platform "
            + "is about the answer.",
            "GO ▸"),
        new TourStop(
            HintCatalog.DashboardScreen,
            "THIS IS WHERE THEY LAND",
            "The same figures, gathered on one screen. Variance is on by default — a tile wired to "
            + "a source with no sigma is left blank rather than drawn as if it were certain.",
            "FINISH"),
    ];

    public static bool HasStop(int screenIndex) => Route.Any(stop => stop.Screen == screenIndex);
}
