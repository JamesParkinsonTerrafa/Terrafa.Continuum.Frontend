// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.RegularExpressions;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// The pointer-hint contract: every tip on a screen goes up together, each closes on its own, and
/// the key takes down the lot. Turning the key back on has to restore what was closed, or it would
/// go down over a screen with nothing left to show. A screen auto-shows once per launch.
/// </summary>
[Collection("workspace")]
public class PointerHintTests : IDisposable
{
    public void Dispose() => PointerHintSettings.ResetForTests();

    [Fact]
    public void MarkVisited_IsTrueOnlyOnTheFirstCall()
    {
        PointerHintSettings.ResetForTests();

        Assert.True(PointerHintSettings.MarkVisited(HintCatalog.TransferFunctionScreen));
        Assert.False(PointerHintSettings.MarkVisited(HintCatalog.TransferFunctionScreen));
        Assert.True(PointerHintSettings.HasVisited(HintCatalog.TransferFunctionScreen));
    }

    [Fact]
    public void MarkVisited_TracksEachScreenSeparately()
    {
        PointerHintSettings.ResetForTests();

        PointerHintSettings.MarkVisited(HintCatalog.TransferFunctionScreen);

        Assert.False(PointerHintSettings.HasVisited(HintCatalog.DashboardScreen));
        Assert.True(PointerHintSettings.MarkVisited(HintCatalog.DashboardScreen));
    }

    [Fact]
    public void SetEnabled_RaisesChangedOnlyOnARealChange()
    {
        PointerHintSettings.ResetForTests();
        var raised = 0;
        void Count() => raised++;
        PointerHintSettings.Changed += Count;

        try
        {
            PointerHintSettings.SetEnabled(true);
            PointerHintSettings.SetEnabled(true);
            PointerHintSettings.SetEnabled(false);

            Assert.Equal(2, raised);
            Assert.False(PointerHintSettings.Enabled);
        }
        finally
        {
            PointerHintSettings.Changed -= Count;
        }
    }

    [Fact]
    public void Dismiss_HidesOneTipAndLeavesTheRest()
    {
        PointerHintSettings.ResetForTests();
        PointerHintSettings.SetEnabled(true);

        PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "ResultChart");

        Assert.True(PointerHintSettings.IsDismissed(HintCatalog.TransferFunctionScreen, "ResultChart"));
        Assert.False(PointerHintSettings.IsDismissed(HintCatalog.TransferFunctionScreen, "StackHost"));
        Assert.True(PointerHintSettings.Enabled);
    }

    [Fact]
    public void Dismiss_IsScopedToItsScreen()
    {
        PointerHintSettings.ResetForTests();

        PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "ResultChart");

        Assert.False(PointerHintSettings.IsDismissed(HintCatalog.DashboardScreen, "ResultChart"));
    }

    [Fact]
    public void SetEnabled_TurningOnRestoresEveryDismissedTip()
    {
        PointerHintSettings.ResetForTests();
        PointerHintSettings.SetEnabled(true);
        PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "ResultChart");
        PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "StackHost");

        PointerHintSettings.SetEnabled(false);
        PointerHintSettings.SetEnabled(true);

        Assert.False(PointerHintSettings.IsDismissed(HintCatalog.TransferFunctionScreen, "ResultChart"));
        Assert.False(PointerHintSettings.IsDismissed(HintCatalog.TransferFunctionScreen, "StackHost"));
    }

    [Fact]
    public void Dismiss_RaisesChangedOnceForTheSameTip()
    {
        PointerHintSettings.ResetForTests();
        var raised = 0;
        void Count() => raised++;
        PointerHintSettings.Changed += Count;

        try
        {
            PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "ResultChart");
            PointerHintSettings.Dismiss(HintCatalog.TransferFunctionScreen, "ResultChart");

            Assert.Equal(1, raised);
        }
        finally
        {
            PointerHintSettings.Changed -= Count;
        }
    }

    [Fact]
    public void ResetForTests_ClearsEverySeenScreen()
    {
        PointerHintSettings.MarkVisited(HintCatalog.MapScreen);
        PointerHintSettings.SetEnabled(true);

        PointerHintSettings.ResetForTests();

        Assert.False(PointerHintSettings.Enabled);
        Assert.True(PointerHintSettings.AutoShow);
        Assert.False(PointerHintSettings.HasVisited(HintCatalog.MapScreen));
    }

    /// <summary>
    /// Every screen in the tab strip carries at least one tip, so the tour has no gap in it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void Catalog_CoversEveryScreen(int screen, string _)
    {
        Assert.NotEmpty(HintCatalog.For(screen));
    }

    /// <summary>
    /// A bubble resolves its target by x:Name against the view's own name scope, so a rename in the
    /// axaml — or a tip filed against the wrong screen — would otherwise drop that bubble in silence
    /// rather than fail the build.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void Catalog_TargetsExistInTheScreenTheyAreFiledUnder(int screen, string viewFileName)
    {
        var markup = File.ReadAllText(ViewPath(viewFileName));
        var names = Regex.Matches(markup, @"x:Name=""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var hint in HintCatalog.For(screen))
        {
            Assert.Contains(hint.TargetName, names);
        }
    }

    public static TheoryData<int, string> Screens => new()
    {
        { HintCatalog.NetworkScreen, "NetworkView.axaml" },
        { HintCatalog.TransferFunctionScreen, "TransferFunctionView.axaml" },
        { HintCatalog.DashboardScreen, "DashboardView.axaml" },
        { HintCatalog.DataTreeScreen, "DbTreeView.axaml" },
        { HintCatalog.MapScreen, "SiteMapView.axaml" },
        { HintCatalog.DataSourcesScreen, "DataSourcesView.axaml" },
        { HintCatalog.CsvExportScreen, "CsvExportView.axaml" }
    };

    private static string ViewPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory.FullName, "src", "Terrafa.Continuum.Frontend.Ui", "Views", fileName);
    }
}
