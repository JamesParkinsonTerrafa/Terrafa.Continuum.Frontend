// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// The tour contract: it opens once a launch on the screen its route starts from, each key press
/// asks the shell for the next stop's screen, and the end of the route hands over to that screen's
/// own pointer tips. Screens the tour still owes keep their tips down until then.
/// </summary>
[Collection("workspace")]
public class TourTests : IDisposable
{
    public void Dispose()
    {
        TourGuide.ResetForTests();
        TourSettings.ResetForTests();
        PointerHintSettings.ResetForTests();
    }

    [Fact]
    public void DropSettings_HoldToTheirRange()
    {
        TourSettings.ResetForTests();

        TourSettings.SetDropHeight(TourSettings.MaxDropHeight * 2);
        TourSettings.SetBounce(1.4);
        TourSettings.SetFallSpeed(0);

        Assert.Equal(TourSettings.MaxDropHeight, TourSettings.DropHeight);
        Assert.Equal(TourSettings.MaxBounce, TourSettings.Bounce);
        Assert.Equal(TourSettings.MinFallSpeed, TourSettings.FallSpeed);
    }

    /// <summary>
    /// A ball that keeps under all of its speed off the floor has to run out of hops. The bounce
    /// setting is capped below 1 for exactly this reason — at 1 the card would bounce for ever.
    /// </summary>
    [Fact]
    public void DropSettings_BounceAlwaysLosesEnergy()
    {
        TourSettings.ResetForTests();
        TourSettings.SetBounce(TourSettings.MaxBounce);

        Assert.True(TourSettings.Bounce < 1);

        var speed = Math.Sqrt(2 * TourSettings.Gravity * TourSettings.MaxDropHeight);
        var hops = 0;
        while (speed >= TourSettings.RestVelocity)
        {
            speed *= TourSettings.Bounce;
            hops++;
        }

        Assert.InRange(hops, 1, 60);
    }

    /// <summary>
    /// The route down the stack: the map, the dashboard they land on, the network that carries
    /// them, the fork to the readings and the workings, and the export at the end.
    /// </summary>
    [Fact]
    public void Route_RunsDownTheStackAndEndsOnAScreenWithTips()
    {
        Assert.Equal(HintCatalog.MapScreen, TourCatalog.StartScreen);
        Assert.Equal(
            [HintCatalog.MapScreen, HintCatalog.DashboardScreen, HintCatalog.NetworkScreen],
            TourCatalog.Opening.Select(stop => stop.Screen));
        Assert.Equal(
            new[] { HintCatalog.DataTreeScreen, HintCatalog.TransferFunctionScreen }.Order(),
            TourCatalog.Branches.Select(stop => stop.Screen).Order());
        Assert.Equal([HintCatalog.CsvExportScreen], TourCatalog.Closing.Select(stop => stop.Screen));
        Assert.NotEmpty(HintCatalog.For(TourCatalog.Closing[^1].Screen));
    }

    /// <summary>Only the network forks, and its keys are the two screens the branches are filed on.</summary>
    [Fact]
    public void Route_ForksOnceOnTheNetwork()
    {
        var forks = TourCatalog.Opening
            .Concat(TourCatalog.Branches)
            .Concat(TourCatalog.Closing)
            .Where(stop => stop.Choices is { Count: > 0 })
            .ToList();

        var fork = Assert.Single(forks);
        Assert.Equal(HintCatalog.NetworkScreen, fork.Screen);
        Assert.Equal(
            TourCatalog.Branches.Select(stop => stop.Screen).Order(),
            fork.Choices!.Select(choice => choice.Screen).Order());
        Assert.All(fork.Choices!, choice => Assert.NotEmpty(choice.Label));
    }

    [Fact]
    public void StartOnce_OpensOnlyOnTheFirstStopsScreen()
    {
        TourGuide.ResetForTests();

        Assert.False(TourGuide.StartOnce(HintCatalog.NetworkScreen));
        Assert.False(TourGuide.IsRunning);
        Assert.True(TourGuide.StartOnce(TourCatalog.StartScreen));
        Assert.Equal(0, TourGuide.StepIndex);
    }

    [Fact]
    public void StartOnce_DoesNotReopenAfterTheTourHasBeenSkipped()
    {
        TourGuide.ResetForTests();
        TourGuide.StartOnce(TourCatalog.StartScreen);
        TourGuide.Skip();

        Assert.False(TourGuide.StartOnce(TourCatalog.StartScreen));
        Assert.False(TourGuide.IsRunning);
    }

    [Fact]
    public void StopOn_ShowsTheCardOnItsOwnScreenOnly()
    {
        TourGuide.ResetForTests();
        TourGuide.StartOnce(TourCatalog.StartScreen);

        Assert.NotNull(TourGuide.StopOn(TourCatalog.StartScreen));
        Assert.Null(TourGuide.StopOn(TourCatalog.Opening[1].Screen));
    }

    [Fact]
    public void Advance_AsksTheShellForTheNextStopsScreen()
    {
        TourGuide.ResetForTests();
        var requested = new List<int>();
        TourGuide.NavigateRequested += requested.Add;
        TourGuide.StartOnce(TourCatalog.StartScreen);

        TourGuide.Advance();

        Assert.Equal([TourCatalog.Opening[1].Screen], requested);
        Assert.NotNull(TourGuide.StopOn(TourCatalog.Opening[1].Screen));
    }

    /// <summary>A stop that forks has no single next screen, so the plain key does nothing on it.</summary>
    [Fact]
    public void Advance_StandsStillOnAStopThatForks()
    {
        TourGuide.ResetForTests();
        var fork = Walk(TourGuide.Advance, TourGuide.Advance);

        Assert.Equal(HintCatalog.NetworkScreen, TourGuide.Plan[TourGuide.StepIndex].Screen);
        Assert.Equal([HintCatalog.DashboardScreen, HintCatalog.NetworkScreen], fork);

        TourGuide.Advance();

        Assert.Equal(HintCatalog.NetworkScreen, TourGuide.Plan[TourGuide.StepIndex].Screen);
    }

    /// <summary>
    /// Either key off the fork walks both of its screens — the one picked first, then the one left
    /// — and both roads meet on the same closing stops, so nothing is missed either way.
    /// </summary>
    [Theory]
    [InlineData(HintCatalog.DataTreeScreen, HintCatalog.TransferFunctionScreen)]
    [InlineData(HintCatalog.TransferFunctionScreen, HintCatalog.DataTreeScreen)]
    public void Choose_TakesTheBranchPickedAndThenTheOtherOne(int picked, int left)
    {
        TourGuide.ResetForTests();
        var visited = Walk(
            TourGuide.Advance,
            TourGuide.Advance,
            () => TourGuide.Choose(picked),
            TourGuide.Advance,
            TourGuide.Advance);

        Assert.Equal(
            [
                HintCatalog.DashboardScreen, HintCatalog.NetworkScreen, picked, left,
                HintCatalog.CsvExportScreen
            ],
            visited);
        Assert.Equal(TourCatalog.Length, TourGuide.Plan.Count);
    }

    /// <summary>A key that is not on the card in front of you cannot move the tour.</summary>
    [Fact]
    public void Choose_IgnoresAScreenTheForkDoesNotOffer()
    {
        TourGuide.ResetForTests();
        Walk(TourGuide.Advance, TourGuide.Advance);

        TourGuide.Choose(HintCatalog.DataSourcesScreen);

        Assert.Equal(HintCatalog.NetworkScreen, TourGuide.Plan[TourGuide.StepIndex].Screen);
        Assert.Equal(TourCatalog.Opening.Count, TourGuide.Plan.Count);
    }

    /// <summary>
    /// The tour is the way in and the tips are the detail, so running off the end of the route has
    /// to leave something behind rather than clearing the screen.
    /// </summary>
    [Fact]
    public void Advance_PastTheLastStopHandsOverToThatScreensTips()
    {
        TourGuide.ResetForTests();
        PointerHintSettings.ResetForTests();
        Walk(
            TourGuide.Advance,
            TourGuide.Advance,
            () => TourGuide.Choose(HintCatalog.DataTreeScreen),
            TourGuide.Advance,
            TourGuide.Advance,
            TourGuide.Advance);

        Assert.False(TourGuide.IsRunning);
        Assert.True(PointerHintSettings.Enabled);
    }

    /// <summary>Opens the tour, runs the given presses, and reports the screens the shell was sent to.</summary>
    private static List<int> Walk(params Action[] presses)
    {
        var requested = new List<int>();
        TourGuide.ResetForTests();
        TourGuide.NavigateRequested += requested.Add;
        TourGuide.StartOnce(TourCatalog.StartScreen);
        foreach (var press in presses) press();
        return requested;
    }

    [Fact]
    public void Skip_LeavesTheTipsWhereTheyWere()
    {
        TourGuide.ResetForTests();
        PointerHintSettings.ResetForTests();
        TourGuide.StartOnce(TourCatalog.StartScreen);

        TourGuide.Skip();

        Assert.False(TourGuide.IsRunning);
        Assert.False(PointerHintSettings.Enabled);
    }

    [Fact]
    public void Owns_HoldsBackTheTipsOnTourScreensUntilTheTourIsDone()
    {
        TourGuide.ResetForTests();

        Assert.True(TourGuide.Owns(TourCatalog.StartScreen));
        Assert.False(TourGuide.Owns(HintCatalog.DataSourcesScreen));

        TourGuide.StartOnce(TourCatalog.StartScreen);
        Assert.True(TourGuide.Owns(TourCatalog.Closing[^1].Screen));

        TourGuide.Skip();
        Assert.False(TourGuide.Owns(TourCatalog.StartScreen));
    }

    [Fact]
    public void AutoStart_OffKeepsTheCardDown()
    {
        TourGuide.ResetForTests();
        TourGuide.AutoStart = false;

        Assert.False(TourGuide.StartOnce(TourCatalog.StartScreen));
        Assert.False(TourGuide.IsRunning);
    }
}
