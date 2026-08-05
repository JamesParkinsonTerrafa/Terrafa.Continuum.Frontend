// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>One of the ways on from a stop that forks: the screen it takes, and the key's label.</summary>
public sealed record TourChoice(int Screen, string Label);

/// <summary>
/// One stop on the guided tour: the screen it is shown over, what it says, and the label on the
/// key that moves on. A stop that forks carries <see cref="Choices"/> instead — two keys, and the
/// branch not taken is visited straight after the one that is. The tour is the way in — the
/// pointer tips on each screen are the detail, and the last stop hands over to them.
/// </summary>
public sealed record TourStop(
    int Screen,
    string Title,
    string Body,
    string ActionLabel,
    IReadOnlyList<TourChoice>? Choices = null);

public static class TourCatalog
{
    /// <summary>
    /// The stops every tour opens with, in order. The tour opens on the first one's screen, and
    /// each key press moves to the next — so the order here is the order the screens are visited
    /// in. The last of them forks, and <see cref="Branches"/> is what follows it.
    /// </summary>
    public static readonly IReadOnlyList<TourStop> Opening =
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
            "GO ▸"),
        new TourStop(
            HintCatalog.NetworkScreen,
            "AND THIS IS WHAT CARRIES THEM",
            "Wire the readings together and the doubt is carried with them, all the way to the "
            + "figure at the end. Two roads lead off this screen: down to the readings themselves, "
            + "or on to the workings that turn them into something worth reporting. Take either — "
            + "the tour comes back for the other.",
            string.Empty,
            [
                new TourChoice(HintCatalog.DataTreeScreen, "SEE THE DATA TREE ▸"),
                new TourChoice(HintCatalog.TransferFunctionScreen, "SEE THE WORKINGS ▸")
            ]),
    ];

    /// <summary>
    /// The stop shown on each of the two screens the network forks to. Both are always visited —
    /// the choice settles only which of them comes first.
    /// </summary>
    public static readonly IReadOnlyList<TourStop> Branches =
    [
        new TourStop(
            HintCatalog.DataTreeScreen,
            "THE GROUND EVERYTHING STANDS ON",
            "Under every figure is a reading that was actually taken. Nothing here is ever "
            + "overwritten — the log only grows, so you can wind back to any moment and see "
            + "exactly what was known at the time.",
            "CONTINUE TOUR ▸"),
        new TourStop(
            HintCatalog.TransferFunctionScreen,
            "THE WORKINGS, IN THE OPEN",
            "Stack proven steps into whatever your business actually measures. Every input is "
            + "picked by hand and stays on show, and the platform says when the maths will not "
            + "support the answer rather than handing you a number anyway.",
            "CONTINUE TOUR ▸"),
    ];

    /// <summary>The stops both roads meet on again, once the fork has been walked both ways.</summary>
    public static readonly IReadOnlyList<TourStop> Closing =
    [
        new TourStop(
            HintCatalog.CsvExportScreen,
            "AND OUT AGAIN, WITH ITS DOUBT",
            "The figures leave the platform the way they were held in it: every column carries the "
            + "band that belongs to it, so what you send on is as honest as what you saw.",
            "FINISH"),
    ];

    /// <summary>How many stops a walk of the tour visits — both branches, in whichever order.</summary>
    public static int Length => Opening.Count + Branches.Count + Closing.Count;

    /// <summary>The screen the tour opens on.</summary>
    public static int StartScreen => Opening[0].Screen;

    /// <summary>The stop filed against one of the fork's two screens.</summary>
    public static TourStop Branch(int screenIndex) =>
        Branches.First(stop => stop.Screen == screenIndex);

    public static bool HasStop(int screenIndex) =>
        Opening.Concat(Branches).Concat(Closing).Any(stop => stop.Screen == screenIndex);
}
