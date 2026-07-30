using System.Diagnostics;

namespace Terrafa.Continuum.Frontend.Controls;

internal readonly record struct BubblePopHandoff(
    string Label, int PoppingIndex, int PreviousIndex, double Position, double Pressure);

internal static class BubbleHandoff
{
    private const double FreshSeconds = 0.25;

    private static BubblePopHandoff? pending;
    private static long recordedAt;

    public static void Record(string label, int poppingIndex, int previousIndex, double position, double pressure)
    {
        pending = new BubblePopHandoff(label, poppingIndex, previousIndex, position, pressure);
        recordedAt = Stopwatch.GetTimestamp();
    }

    public static bool TryTake(IReadOnlyList<string> labels, int activeIndex, out BubblePopHandoff handoff)
    {
        handoff = default;
        if (pending is not { } candidate) return false;

        var ageSeconds = (Stopwatch.GetTimestamp() - recordedAt) / (double)Stopwatch.Frequency;
        if (ageSeconds > FreshSeconds)
        {
            pending = null;
            return false;
        }
        if (candidate.PoppingIndex != activeIndex) return false;
        if (candidate.PoppingIndex >= labels.Count || labels[candidate.PoppingIndex] != candidate.Label) return false;

        pending = null;
        handoff = candidate;
        return true;
    }
}
