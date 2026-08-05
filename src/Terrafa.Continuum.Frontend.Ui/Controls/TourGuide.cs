// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// Where the guided tour has got to. The card itself is drawn by <see cref="TourLayer"/>, which
/// sits on every screen and shows the stop filed against that screen — moving on is therefore a
/// step here plus a navigation the shell performs, not something a view does to itself.
/// The route forks once, so the order of the stops is settled as the tour is walked rather than
/// laid out in advance: <see cref="Plan"/> is the road actually taken.
/// </summary>
public static class TourGuide
{
    private static readonly List<TourStop> plan = [];

    /// <summary>Off for the snapshot suite, where a fresh view must not open the tour over the frame.</summary>
    public static bool AutoStart { get; set; } = true;

    /// <summary>Index into <see cref="Plan"/>, or -1 when the tour is not up.</summary>
    public static int StepIndex { get; private set; } = -1;

    /// <summary>The stops laid down so far, in the order they are walked.</summary>
    public static IReadOnlyList<TourStop> Plan => plan;

    public static bool IsRunning => StepIndex >= 0;

    /// <summary>The tour opens once a launch, however many times its screen is built.</summary>
    public static bool HasRun { get; private set; }

    public static event Action? Changed;

    public static event Action<int>? NavigateRequested;

    public static TourStop? StopOn(int screenIndex) =>
        IsRunning && plan[StepIndex].Screen == screenIndex ? plan[StepIndex] : null;

    /// <summary>
    /// A screen the tour has still to speak over. Its own pointer tips stay down until then, so the
    /// two never come up together.
    /// </summary>
    public static bool Owns(int screenIndex) =>
        (!HasRun || IsRunning) && TourCatalog.HasStop(screenIndex);

    public static bool StartOnce(int screenIndex)
    {
        if (!AutoStart || HasRun || TourCatalog.Opening.Count == 0) return false;
        if (TourCatalog.StartScreen != screenIndex) return false;

        HasRun = true;
        plan.Clear();
        plan.AddRange(TourCatalog.Opening);
        StepIndex = 0;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Takes one of the roads off a stop that forks. Both are walked either way: the branch picked
    /// is laid down first and the one left is laid down behind it, so whichever key is pressed the
    /// tour reaches the closing stops having missed nothing.
    /// </summary>
    public static void Choose(int screenIndex)
    {
        if (!IsRunning || plan[StepIndex].Choices is not { Count: > 0 } choices) return;
        if (choices.All(choice => choice.Screen != screenIndex)) return;

        plan.Add(TourCatalog.Branch(screenIndex));
        foreach (var choice in choices.Where(choice => choice.Screen != screenIndex))
            plan.Add(TourCatalog.Branch(choice.Screen));
        plan.AddRange(TourCatalog.Closing);

        Step(StepIndex + 1);
    }

    /// <summary>
    /// Moves to the next stop and asks the shell for its screen; finishes at the end of the route.
    /// A stop that forks is left by <see cref="Choose"/> instead — it has no single next screen.
    /// </summary>
    public static void Advance()
    {
        if (!IsRunning || plan[StepIndex].Choices is { Count: > 0 }) return;
        var next = StepIndex + 1;
        if (next >= plan.Count)
        {
            Finish();
            return;
        }

        Step(next);
    }

    private static void Step(int index)
    {
        StepIndex = index;
        Changed?.Invoke();
        NavigateRequested?.Invoke(plan[index].Screen);
    }

    /// <summary>
    /// Reaching the end puts the pointer tips up on the screen it ended on: the tour has said why
    /// the screen matters, and the tips take over pointing at the parts of it.
    /// </summary>
    public static void Finish()
    {
        if (!IsRunning) return;
        var screen = plan[StepIndex].Screen;
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
        plan.Clear();
        Changed = null;
        NavigateRequested = null;
    }
}
