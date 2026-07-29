using System.Globalization;

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>
/// Fills in the numerics a chart needs for measures that were declared with display strings only.
///
/// Every leaf the app currently owns is demo data — either hand-written in
/// <see cref="SiteAlpha"/> or built by the stub catalogue — and both spell a reading as "8,410 bbl"
/// with "± 92" beside it. This is the one place that turns those into numbers, called wherever a
/// leaf first learns its path. A real catalogue sets Value/Sigma/History on the wire and never
/// comes through here: <see cref="Hydrate"/> leaves anything already carrying a value untouched.
///
/// Formats not written by that demo data deliberately fall out as NaN, which is what keeps
/// "EN590", "—" and exact counts genuinely variance-free rather than giving them a
/// plausible-looking σ the dashboard would then happily plot.
/// </summary>
public static class MeasureNumerics
{
    private const int HistoryLength = 24;

    /// <summary>The child leaf name that carries a measure's σ, as in "temp.sigma".</summary>
    public const string SigmaLeafName = "sigma";

    /// <summary>
    /// Folds every "sigma" child leaf into its parent measure, so a tree that states its
    /// uncertainty as its own leaf — the shape a real feed uses for a heteroscedastic σ(x) — ends
    /// up indistinguishable to a chart from one that states it inline.
    ///
    /// Run once over a finished tree: a leaf cannot inspect its own children while being built.
    /// A σ child wins over an inline <see cref="Measure.SigmaDisplay"/>, since it is structured
    /// data with a series behind it rather than a string that had to be parsed.
    /// </summary>
    public static void BindSigmaLeaves(DataTreeNode root)
    {
        foreach (var node in root.Descendants())
        {
            if (node.Kind != DataNodeKind.Measure || node.Reading is not { } reading) continue;

            var carrier = node.Children.FirstOrDefault(child =>
                child.Kind == DataNodeKind.Measure &&
                child.Name.Equals(SigmaLeafName, StringComparison.OrdinalIgnoreCase));
            if (carrier?.Reading is not { } carrierReading || !carrierReading.HasValue) continue;

            // Regenerated with a wider wobble than the default 0.6%: a σ that varies per reading is
            // the whole reason to carry it as a leaf, and the flat default would draw a band
            // indistinguishable from an inline scalar.
            var carrierHistory = History(carrier.Path, carrierReading.Value, Math.Abs(carrierReading.Value) * 0.18);
            carrier.Reading = With(carrierReading, carrierHistory, isSigmaCarrier: true);
            node.Reading = new Measure
            {
                Display = reading.Display,
                SigmaDisplay = reading.SigmaDisplay.Length > 0
                    ? reading.SigmaDisplay
                    : $"± {carrierReading.Display}",
                SigmaKind = reading.SigmaKind,
                Detail = reading.Detail,
                Selected = reading.Selected,
                IsNew = reading.IsNew,
                IsVector = reading.IsVector,
                Value = reading.Value,
                Sigma = carrierReading.Value,
                Unit = reading.Unit,
                History = reading.History,
                SigmaHistory = carrierHistory
            };
        }
    }

    private static Measure With(Measure source, IReadOnlyList<double> history, bool isSigmaCarrier) => new()
    {
        Display = source.Display,
        SigmaDisplay = source.SigmaDisplay,
        SigmaKind = source.SigmaKind,
        Detail = source.Detail,
        Selected = source.Selected,
        IsNew = source.IsNew,
        IsVector = source.IsVector,
        Value = source.Value,
        Sigma = source.Sigma,
        Unit = source.Unit,
        History = history,
        SigmaHistory = source.SigmaHistory,
        IsSigmaCarrier = isSigmaCarrier
    };

    /// <summary>Returns <paramref name="reading"/> with numerics derived from its display strings.</summary>
    public static Measure Hydrate(Measure reading, string path)
    {
        if (reading.HasValue) return reading;

        var (value, unit) = ParseValue(reading.Display);
        var sigma = ParseSigma(reading.SigmaDisplay);
        if (double.IsNaN(value)) return reading;

        return new Measure
        {
            Display = reading.Display,
            SigmaDisplay = reading.SigmaDisplay,
            SigmaKind = reading.SigmaKind,
            Detail = reading.Detail,
            Selected = reading.Selected,
            IsNew = reading.IsNew,
            IsVector = reading.IsVector,
            Value = value,
            Sigma = sigma,
            Unit = unit,
            History = History(path, value, sigma)
        };
    }

    /// <summary>Splits a reading such as "8,410 bbl" into its number and its unit.</summary>
    public static (double Value, string Unit) ParseValue(string display)
    {
        var text = display.Trim();
        if (text.Length == 0) return (double.NaN, "");

        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is ',' or '.' or '-' or '+'))
            end++;
        if (end == 0) return (double.NaN, "");

        // "04:00–09:00" leads with digits but is a window, not a quantity.
        if (end < text.Length && text[end] == ':') return (double.NaN, "");

        var number = text[..end].Replace(",", "");
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (value, text[end..].Trim())
            : (double.NaN, "");
    }

    /// <summary>
    /// Reads "± 92" as 92. Everything else — "½ spread", "exact", "" — is NaN, so leaves that
    /// carry no variance stay that way instead of acquiring one on the way to a chart.
    /// </summary>
    public static double ParseSigma(string sigmaDisplay)
    {
        var text = sigmaDisplay.Trim();
        if (!text.StartsWith('±')) return double.NaN;
        var (value, _) = ParseValue(text[1..].Trim());
        return value;
    }

    /// <summary>
    /// A stable series to draw behind the reading. Seeded from the leaf path so a snapshot renders
    /// identically on every run — an unseeded <see cref="Random"/> would make the PNGs differ on
    /// each build and turn the snapshot check into noise.
    /// </summary>
    public static IReadOnlyList<double> History(string path, double value, double sigma)
    {
        if (double.IsNaN(value)) return [];

        var scale = double.IsNaN(sigma) || sigma <= 0 ? Math.Abs(value) * 0.006 : sigma;
        if (scale <= 0) scale = 1;

        var state = Fnv1a(path);
        var series = new double[HistoryLength];
        var drift = 0.0;
        for (var i = 0; i < HistoryLength; i++)
        {
            drift += (NextUnit(ref state) - 0.5) * scale * 0.9;
            series[i] = value + drift + (NextUnit(ref state) - 0.5) * scale;
        }

        // Land the series on the reading itself, so the last point is the value the tree shows.
        var correction = value - series[^1];
        for (var i = 0; i < HistoryLength; i++)
            series[i] += correction * i / (HistoryLength - 1.0);
        return series;
    }

    private static uint Fnv1a(string text)
    {
        // String.GetHashCode is randomised per process, which would defeat the point.
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return hash == 0 ? 1u : hash;
    }

    private static double NextUnit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state / (double)uint.MaxValue;
    }
}
