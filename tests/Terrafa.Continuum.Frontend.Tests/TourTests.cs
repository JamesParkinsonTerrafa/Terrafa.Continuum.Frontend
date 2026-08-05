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

    [Fact]
    public void Route_OpensOnTheMapAndEndsOnAScreenWithTips()
    {
        Assert.NotEmpty(TourCatalog.Route);
        Assert.Equal(HintCatalog.MapScreen, TourCatalog.Route[0].Screen);
        Assert.NotEmpty(HintCatalog.For(TourCatalog.Route[^1].Screen));
    }

    [Fact]
    public void StartOnce_OpensOnlyOnTheFirstStopsScreen()
    {
        TourGuide.ResetForTests();

        Assert.False(TourGuide.StartOnce(HintCatalog.NetworkScreen));
        Assert.False(TourGuide.IsRunning);
        Assert.True(TourGuide.StartOnce(TourCatalog.Route[0].Screen));
        Assert.Equal(0, TourGuide.StepIndex);
    }

    [Fact]
    public void StartOnce_DoesNotReopenAfterTheTourHasBeenSkipped()
    {
        TourGuide.ResetForTests();
        TourGuide.StartOnce(TourCatalog.Route[0].Screen);
        TourGuide.Skip();

        Assert.False(TourGuide.StartOnce(TourCatalog.Route[0].Screen));
        Assert.False(TourGuide.IsRunning);
    }

    [Fact]
    public void StopOn_ShowsTheCardOnItsOwnScreenOnly()
    {
        TourGuide.ResetForTests();
        TourGuide.StartOnce(TourCatalog.Route[0].Screen);

        Assert.NotNull(TourGuide.StopOn(TourCatalog.Route[0].Screen));
        Assert.Null(TourGuide.StopOn(TourCatalog.Route[1].Screen));
    }

    [Fact]
    public void Advance_AsksTheShellForTheNextStopsScreen()
    {
        TourGuide.ResetForTests();
        var requested = new List<int>();
        TourGuide.NavigateRequested += requested.Add;
        TourGuide.StartOnce(TourCatalog.Route[0].Screen);

        TourGuide.Advance();

        Assert.Equal([TourCatalog.Route[1].Screen], requested);
        Assert.NotNull(TourGuide.StopOn(TourCatalog.Route[1].Screen));
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
        TourGuide.StartOnce(TourCatalog.Route[0].Screen);

        for (var step = 0; step < TourCatalog.Route.Count; step++) TourGuide.Advance();

        Assert.False(TourGuide.IsRunning);
        Assert.True(PointerHintSettings.Enabled);
    }

    [Fact]
    public void Skip_LeavesTheTipsWhereTheyWere()
    {
        TourGuide.ResetForTests();
        PointerHintSettings.ResetForTests();
        TourGuide.StartOnce(TourCatalog.Route[0].Screen);

        TourGuide.Skip();

        Assert.False(TourGuide.IsRunning);
        Assert.False(PointerHintSettings.Enabled);
    }

    [Fact]
    public void Owns_HoldsBackTheTipsOnTourScreensUntilTheTourIsDone()
    {
        TourGuide.ResetForTests();

        Assert.True(TourGuide.Owns(TourCatalog.Route[0].Screen));
        Assert.False(TourGuide.Owns(HintCatalog.NetworkScreen));

        TourGuide.StartOnce(TourCatalog.Route[0].Screen);
        Assert.True(TourGuide.Owns(TourCatalog.Route[^1].Screen));

        TourGuide.Skip();
        Assert.False(TourGuide.Owns(TourCatalog.Route[0].Screen));
    }

    [Fact]
    public void AutoStart_OffKeepsTheCardDown()
    {
        TourGuide.ResetForTests();
        TourGuide.AutoStart = false;

        Assert.False(TourGuide.StartOnce(TourCatalog.Route[0].Screen));
        Assert.False(TourGuide.IsRunning);
    }
}
