// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// Where the guided tour has got to. The card itself is drawn by <see cref="TourLayer"/>, which
/// sits on every screen and shows the stop filed against that screen — moving on is therefore a
/// step here plus a navigation the shell performs, not something a view does to itself.
/// </summary>
public static class TourGuide
{
    /// <summary>Off for the snapshot suite, where a fresh view must not open the tour over the frame.</summary>
    public static bool AutoStart { get; set; } = true;

    /// <summary>Index into <see cref="TourCatalog.Route"/>, or -1 when the tour is not up.</summary>
    public static int StepIndex { get; private set; } = -1;

    public static bool IsRunning => StepIndex >= 0;

    /// <summary>The tour opens once a launch, however many times its screen is built.</summary>
    public static bool HasRun { get; private set; }

    public static event Action? Changed;

    public static event Action<int>? NavigateRequested;

    public static TourStop? StopOn(int screenIndex) =>
        IsRunning && TourCatalog.Route[StepIndex].Screen == screenIndex
            ? TourCatalog.Route[StepIndex]
            : null;

    /// <summary>
    /// A screen the tour has still to speak over. Its own pointer tips stay down until then, so the
    /// two never come up together.
    /// </summary>
    public static bool Owns(int screenIndex) =>
        (!HasRun || IsRunning) && TourCatalog.HasStop(screenIndex);

    public static bool StartOnce(int screenIndex)
    {
        if (!AutoStart || HasRun || TourCatalog.Route.Count == 0) return false;
        if (TourCatalog.Route[0].Screen != screenIndex) return false;

        HasRun = true;
        StepIndex = 0;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Moves to the next stop and asks the shell for its screen; finishes at the end of the route.</summary>
    public static void Advance()
    {
        if (!IsRunning) return;
        var next = StepIndex + 1;
        if (next >= TourCatalog.Route.Count)
        {
            Finish();
            return;
        }

        StepIndex = next;
        Changed?.Invoke();
        NavigateRequested?.Invoke(TourCatalog.Route[next].Screen);
    }

    /// <summary>
    /// Reaching the end puts the pointer tips up on the screen it ended on: the tour has said why
    /// the screen matters, and the tips take over pointing at the parts of it.
    /// </summary>
    public static void Finish()
    {
        if (!IsRunning) return;
        var screen = TourCatalog.Route[StepIndex].Screen;
        StepIndex = -1;
        Changed?.Invoke();
        if (HintCatalog.For(screen).Count > 0) PointerHintSettings.SetEnabled(true);
    }

    /// <summary>Closing the card drops the tour where it stands and shows nothing in its place.</summary>
    public static void Skip()
    {
        if (!IsRunning) return;
        StepIndex = -1;
        Changed?.Invoke();
    }

    public static void ResetForTests()
    {
        StepIndex = -1;
        HasRun = false;
        AutoStart = true;
        Changed = null;
        NavigateRequested = null;
    }
}
