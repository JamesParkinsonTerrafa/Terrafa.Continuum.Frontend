using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

public sealed class StaticDataFeed : IDataFeed
{
    public DataSnapshot Current { get; } = DemoData.CreateSnapshot();

    public event EventHandler<DataSnapshot>? SnapshotChanged
    {
        add { }
        remove { }
    }
}

public static class DemoData
{
    public static DataSnapshot CreateSnapshot()
    {
        var site = new SiteAlpha();
        var tree = DataTreeBuilder.Build(site, "SITE_ALPHA");

        var positions = new List<PositionRow>
        {
            new("DIESEL EN590", "24,085", "±152", "+1.2%", true, [4, 6, 5, 9, 8, 13, 14]),
            new("JET A-1", "11,410", "±88", "−0.8%", false, [13, 11, 12, 8, 9, 5, 4]),
            new("GASOIL 0.1%", "6,932", "±61", "+0.3%", true, [7, 8, 6, 8, 10, 9, 11]),
            new("FAME-0 (BIO)", "2,204", "±40", "−2.1%", false, [14, 12, 10, 11, 7, 6, 3])
        };

        var leaderboard = new List<LeaderboardRow>
        {
            new("1", "flow_v3 · sharp", "−1,204.8", "▲ +2.1", 1, false),
            new("2", "flow_v2", "−1,246.3", "— 0.0", 0, false),
            new("3", "expiry_frailty_v1", "−1,311.0", "▼ −4.7", -1, false),
            new("4", "baseline · vague", "−1,502.6", "▼ −1.3", -1, true)
        };

        var calibration = new List<CalibrationPoint>
        {
            new(0.1, 0.085, false),
            new(0.2, 0.165, false),
            new(0.3, 0.268, false),
            new(0.4, 0.344, false),
            new(0.5, 0.465, false),
            new(0.6, 0.532, true),
            new(0.7, 0.607, true),
            new(0.8, 0.741, false),
            new(0.9, 0.866, false)
        };

        var wealthXs = Enumerable.Range(0, 10).Select(i => (double)i).ToArray();
        var wealth = new List<NamedSeries>
        {
            new("flow_v3 — holds", wealthXs, [1.2, 1.5, 1.0, 1.7, 1.4, 1.9, 1.5, 1.8, 1.4, 1.6]),
            new("expiry_frailty_v1 → FALSIFIED · flag for revision", wealthXs, [1.2, 1.9, 2.6, 3.8, 4.7, 7.0, 9.8, 13.5, 16.7, 20.1])
        };

        var survivalXs = Enumerable.Range(0, 7).Select(i => i * 10.0).ToArray();
        var survival = new List<NamedSeries>
        {
            new("Ẑ low (cool batch)", survivalXs, [0.98, 0.955, 0.90, 0.82, 0.71, 0.57, 0.41]),
            new("baseline λ₀", survivalXs, [0.98, 0.92, 0.82, 0.68, 0.51, 0.32, 0.14]),
            new("Ẑ high · 301.2K", survivalXs, [0.98, 0.865, 0.70, 0.48, 0.27, 0.11, 0.04])
        };

        double[] intensityBars = [0.34, 0.28, 0.46, 0.38, 0.62, 0.54, 0.86, 1.02, 1.30, 1.50];
        double[] intensityLine = [0.22, 0.20, 0.32, 0.30, 0.48, 0.44, 0.72, 0.92, 1.18, 1.40];

        var events = new List<EventLogEntry>
        {
            new("14:32:07", "e-1284102", "sensor.level", "→ tank_01.{level} · mark (14,203 bbl)", "cyan"),
            new("14:31:52", "e-1284101", "sensor.temp", "→ tank_01.{temp} · mark (301.2 K)", "cyan"),
            new("14:31:40", "e-1284100", "spoilage.event", "→ tank_01.{spoilage} · mark (12 bbl, EN590)", "red"),
            new("14:30:11", "e-1284099", "meter.flow", "→ berth.meter.{flow} · mark (vec, Σ)", "cyan"),
            new("14:28:56", "e-1284098", "schema.extend", "→ +tank_03.{level,temp} · contract v1.4 held", "green"),
            new("14:27:03", "e-1284097", "sensor.level", "→ tank_02.{level} · mark (9,882 bbl)", "cyan"),
            new("14:25:47", "e-1284096", "external.grade", "→ intake.{grade} · bound via contract", "cyan")
        };

        return new DataSnapshot(
            new DateTimeOffset(2026, 7, 8, 14, 32, 7, TimeSpan.Zero),
            site,
            tree,
            positions,
            leaderboard,
            calibration,
            wealth,
            20.0,
            survival,
            intensityBars,
            intensityLine,
            events,
            1_284_102);
    }
}
