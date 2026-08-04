// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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
    /// The suffix marking a <i>sibling</i> column as another column's σ, as in
    /// "cell_concentration_umol_l__sigma". An Athena table is flat and cannot nest a child leaf
    /// under the measure it belongs to, so a real feed spells the pairing in the column name.
    /// Applied by HttpDatasetCatalog while the rows are still in hand — pairing two series is only
    /// sound while their row indices are known to line up, which is knowledge this binder does not
    /// have. <see cref="BindSigmaLeaves"/> handles the nested spelling only.
    /// </summary>
    public const string SigmaSuffix = "__sigma";

    /// <summary>
    /// Folds every "sigma" child leaf into its parent measure, so a tree that states its
    /// uncertainty as its own leaf — the shape a real feed uses for a heteroscedastic σ(x) — ends
    /// up indistinguishable to a chart from one that states it inline.
    ///
    /// Run once over a finished tree: a leaf cannot inspect its own children while being built.
    /// A σ child wins over an inline <see cref="Measure.SigmaDisplay"/>, since it is structured
    /// data with a series behind it rather than a string that had to be parsed.
    ///
    /// <para>
    /// This is the <i>nested</i> spelling only. A flat Athena table cannot nest, so it spells the
    /// pairing in the column name instead — see <see cref="Services.DatasetSchemaBuilder"/>.
    /// </para>
    /// </summary>
    public static void BindSigmaLeaves(DataTreeNode root)
    {
        foreach (var node in root.Descendants())
        {
            // DeclaredReading, not Reading: this binds the tree in hand, before anything the store
            // holds for these paths is allowed to speak for them.
            if (node.Kind != DataNodeKind.Measure || node.DeclaredReading is not { } reading) continue;

            var carrier = node.Children.FirstOrDefault(child =>
                child.Kind == DataNodeKind.Measure &&
                child.Name.Equals(SigmaLeafName, StringComparison.OrdinalIgnoreCase));
            if (carrier?.DeclaredReading is not { } carrierReading || !carrierReading.HasValue) continue;

            // Regenerated with a wider wobble than the default 0.6%: a σ that varies per reading is
            // the whole reason to carry it as a leaf, and the flat default would draw a band
            // indistinguishable from an inline scalar.
            var carrierHistory = History(carrier.Path, carrierReading.Value, Math.Abs(carrierReading.Value) * 0.18);
            carrier.Reading = carrierReading with { History = carrierHistory, IsSigmaCarrier = true };
            node.Reading = reading with
            {
                SigmaDisplay = reading.SigmaDisplay.Length > 0
                    ? reading.SigmaDisplay
                    : $"± {carrierReading.Display}",
                Sigma = carrierReading.Value,
                SigmaHistory = carrierHistory
            };
        }
    }

    /// <summary>
    /// Returns <paramref name="reading"/> with numerics derived from its display strings.
    /// </summary>
    /// <param name="withHistory">
    /// Whether to put a series behind the reading. True for the hand-written demo tree, whose whole
    /// job is to have something to draw. False for a real catalogue: a sample query returns one row,
    /// and giving that one reading twenty-four invented neighbours would put a fabricated time
    /// series on a chart that looked exactly like a measured one.
    /// </param>
    public static Measure Hydrate(Measure reading, string path, bool withHistory = true)
    {
        if (reading.HasValue) return reading;

        var (value, unit) = ParseValue(reading.Display);
        var sigma = ParseSigma(reading.SigmaDisplay);
        if (double.IsNaN(value)) return reading;

        return reading with
        {
            Value = value,
            Sigma = sigma,
            Unit = unit,
            History = withHistory ? History(path, value, sigma) : []
        };
    }

    /// <summary>
    /// Writes a number the way the demo data spells one, so a reading derived by the network reads
    /// as the same kind of thing as one the tree declared — "24,085", not "24085.000000001".
    /// </summary>
    public static string Format(double value)
    {
        if (double.IsNaN(value)) return "—";
        var magnitude = Math.Abs(value);
        if (magnitude >= 10000) return value.ToString("#,##0", CultureInfo.InvariantCulture);
        if (magnitude >= 100) return value.ToString("0.#", CultureInfo.InvariantCulture);
        if (magnitude >= 1) return value.ToString("0.##", CultureInfo.InvariantCulture);
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A σ, to three significant figures. Quoting a propagated σ to the precision the arithmetic
    /// happens to produce — "± 152.11813" — claims a sharpness the inputs never had.
    /// </summary>
    public static string FormatSigma(double sigma)
    {
        if (double.IsNaN(sigma)) return "";
        if (sigma == 0) return "0";
        var scale = Math.Pow(10, 2 - Math.Floor(Math.Log10(Math.Abs(sigma))));
        return Format(Math.Round(sigma * scale) / scale);
    }

    /// <summary>A determination as text: 0 is false and anything else true — the encoding a
    /// boolean leaf and a comparator share.</summary>
    public static string FormatBoolean(double value) =>
        double.IsNaN(value) ? "—" : value != 0 ? "true" : "false";

    /// <summary>
    /// A σ level, to two significant figures — "2.3σ". Infinite means the spread was exactly
    /// zero: the inputs are exact, and so is the determination.
    /// </summary>
    public static string FormatSigmaLevel(double level)
    {
        if (double.IsNaN(level)) return "";
        if (double.IsPositiveInfinity(level)) return "exact";
        var magnitude = Math.Abs(level);
        if (magnitude == 0) return "0σ";
        var scale = Math.Pow(10, 1 - Math.Floor(Math.Log10(magnitude)));
        return $"{Format(Math.Round(magnitude * scale) / scale)}σ";
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
    /// Reads a determination cell: "true"/"1" as 1, "false"/"0" as 0, anything else as NaN —
    /// a boolean column carrying text that is neither stays valueless rather than guessed at.
    /// </summary>
    public static double ParseBoolean(string display)
    {
        var text = display.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1") return 1;
        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0") return 0;
        return double.NaN;
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
