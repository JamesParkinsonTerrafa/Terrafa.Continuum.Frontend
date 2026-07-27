using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Stand-in for the real catalogue service. Both calls complete synchronously so headless
/// snapshots stay deterministic — swap this for the wire implementation when the backend lands.
/// </summary>
public sealed class StubDatasetCatalog : IDatasetCatalog
{
    public static StubDatasetCatalog Instance { get; } = new();

    private static readonly Dictionary<string, IReadOnlyList<string>> Catalogue = new()
    {
        ["OWN OPERATIONS"] = ["SITE_ALPHA", "SITE_BETA", "LAB_ASSAYS"],
        ["MARKET & PRICING"] = ["ICE_BRENT", "CME_WTI", "FX_ECB", "PRICE_ASSESSMENTS"],
        ["WEATHER & CLIMATE"] = ["MET_STATIONS", "MET_ENSEMBLE"],
        ["FREIGHT & AIS"] = ["AIS_FREIGHT", "PORT_CALLS"],
        ["MACRO & POLICY"] = ["WEEKLY_STOCK_REPORTS", "TARIFF_SCHEDULES"],
        ["REFERENCE & CONTRACTS"] = ["CONTRACT_SPECS", "CALENDARS"]
    };

    private readonly Dictionary<string, DatasetSchema> schemaCache = [];

    /// <summary>The operator's own site — needed synchronously to seed the workspace.</summary>
    public static DatasetSchema SiteAlpha { get; } = new(
        "SITE_ALPHA", "own telemetry", "v1.4", "1 s", "2019-04 → live", "internal",
        DataTreeBuilder.Build(new SiteAlpha(), "SITE_ALPHA"));

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(Catalogue);

    public Task<DatasetSchema> GetSchemaAsync(string dataset)
    {
        if (!schemaCache.TryGetValue(dataset, out var schema))
        {
            schema = BuildSchema(dataset);
            schemaCache[dataset] = schema;
        }
        return Task.FromResult(schema);
    }

    public static string TopicOf(string dataset) =>
        Catalogue.FirstOrDefault(entry => entry.Value.Contains(dataset)).Key ?? "UNCATEGORISED";

    private static DatasetSchema BuildSchema(string dataset) => dataset switch
    {
        "SITE_ALPHA" => SiteAlpha,

        "SITE_BETA" => Schema(dataset, "own telemetry", "v1.4", "1 s", "2022-11 → live", "internal", root =>
        {
            var farm = root.Object("tank_farm");
            farm.Object("tank_11")
                .Leaf("level", "8,410 bbl", "± 92", "σ(x)", "σ(x) heteroscedastic")
                .Leaf("temp", "298.4 K", "± 0.4", "σ", "σ flat");
            farm.Object("tank_12")
                .Leaf("level", "5,102 bbl", "± 71", "σ(x)", "σ(x) heteroscedastic")
                .Leaf("temp", "299.1 K", "± 0.4", "σ", "σ flat");
        }),

        "LAB_ASSAYS" => Schema(dataset, "site lab", "v2.0", "per batch", "2020-01 → live", "internal", root =>
        {
            root.Object("batch", "CERT")
                .Leaf("grade", "EN590", "", "", "categorical · Type B cert")
                .Leaf("sulphur", "8.2 ppm", "± 0.4", "σ", "σ from repeatability study")
                .Leaf("density", "832.1 kg/m³", "± 0.3", "σ", "σ flat · 15 °C basis");
            root.Object("cert").Leaf("type_b", "Type B", "", "", "certificate class");
        }),

        "ICE_BRENT" => Schema(dataset, "ICE ENDEX", "vB.2", "tick → 1 m bars", "2011-01 → live", "derived figures OK", root =>
        {
            root.Object("curve", "CURVE")
                .Leaf("m1_settle", "78.42 USD/bbl", "± 0.02", "½ spread", "restated T+1")
                .Leaf("m1_volume", "41,208 lots", "", "exact", "exact count · no aggregate in tree")
                .Leaf("m2_settle", "78.05 USD/bbl", "± 0.03", "½ spread", "session UTC");
            root.Object("spec").Leaf("grade_spec", "EN590", "", "", "deliverable grade · enum");
            root.Object("session").Leaf("calendar", "UTC", "", "", "exchange session · halts");
        }),

        "CME_WTI" => Schema(dataset, "CME GLOBEX", "vB.2", "tick → 1 m bars", "2009-06 → live", "derived figures OK", root =>
        {
            root.Object("curve", "CURVE")
                .Leaf("m1_settle", "74.18 USD/bbl", "± 0.02", "½ spread", "settle restated T+1")
                .Leaf("m1_volume", "88,140 lots", "", "exact", "exact count");
        }),

        "FX_ECB" => Schema(dataset, "ECB", "v1.0", "daily fixing", "1999-01 → live", "public", root =>
        {
            root.Object("rates")
                .Leaf("eur_usd", "1.0842", "± 0.0001", "σ", "16:00 CET fixing")
                .Leaf("eur_gbp", "0.8531", "± 0.0001", "σ", "16:00 CET fixing");
        }),

        "PRICE_ASSESSMENTS" => Schema(dataset, "3rd party", "v3.1", "daily", "2014-01 → live", "entitlement required", root =>
        {
            root.Object("assessments")
                .Leaf("cif_nwe", "—", "", "", "entitlement required — values withheld")
                .Leaf("fob_med", "—", "", "", "entitlement required — values withheld");
        }),

        "MET_STATIONS" => Schema(dataset, "national met", "vM.7", "hourly obs", "1998-01 → live", "public", root =>
        {
            root.Object("stn_coastal")
                .Leaf("air_temp", "301.6 K", "± 0.5", "σ", "coarsest in this branch · 1 h")
                .Leaf("wind", "6.2 m/s", "± 0.8", "Σ aniso", "2-vector · Σ anisotropic", isVector: true);
            root.Object("stn_inland")
                .Leaf("air_temp", "299.9 K", "± 0.5", "σ", "1 h");
        }),

        "MET_ENSEMBLE" => Schema(dataset, "national met", "vM.7", "6 h cycle", "2016-01 → live", "public", root =>
        {
            root.Object("members", "ENSEMBLE")
                .Leaf("m01_temp", "302.1 K", "", "σ native", "ensemble member")
                .Leaf("m02_temp", "301.4 K", "", "σ native", "ensemble member");
            root.Object("spread").Leaf("sigma_native", "0.9 K", "", "σ native", "σ carried natively — no fit");
        }),

        "AIS_FREIGHT" => Schema(dataset, "AIS aggregator", "v0.9", "30 s", "2017-03 → live", "derived figures OK", root =>
        {
            root.Object("vessels")
                .Leaf("speed", "11.4 kn", "± 0.3", "σ", "over ground")
                .Leaf("draft", "12.1 m", "± 0.2", "σ", "reported, not measured");
            root.Object("berth").Leaf("delivery_window", "04:00–09:00", "", "", "coincident timing only");
        }),

        "PORT_CALLS" => Schema(dataset, "AIS aggregator", "v0.9", "event", "2017-03 → live", "derived figures OK", root =>
        {
            root.Object("calls")
                .Leaf("arrivals", "18 /wk", "", "exact", "exact count")
                .Leaf("departures", "17 /wk", "", "exact", "exact count");
        }),

        "WEEKLY_STOCK_REPORTS" => Schema(dataset, "agency", "v1.2", "weekly", "1990-01 → live", "public · revised", root =>
        {
            root.Object("regions", "REVISED")
                .Leaf("padd_1_stocks", "58.2 Mbbl", "± 0.6", "σ", "revision series — T+2 restatements")
                .Leaf("padd_3_stocks", "231.7 Mbbl", "± 1.4", "σ", "revision series — T+2 restatements");
        }),

        "TARIFF_SCHEDULES" => Schema(dataset, "customs", "v1.0", "quarterly", "2004-01 → live", "public", root =>
        {
            root.Object("lines").Leaf("duty_rate", "3.5 %", "", "", "step function · effective dates");
        }),

        "CONTRACT_SPECS" => Schema(dataset, "reference", "v1.1", "on change", "2011-01 → live", "public", root =>
        {
            root.Object("specs")
                .Leaf("deliverable_grade", "EN590", "", "", "enum · contract-fixed")
                .Leaf("lot_size", "1,000 bbl", "", "exact", "exact");
        }),

        "CALENDARS" => Schema(dataset, "reference", "v1.1", "on change", "2000-01 → live", "public", root =>
        {
            root.Object("sessions")
                .Leaf("holidays", "—", "", "", "calendar · closed days")
                .Leaf("halts", "—", "", "", "calendar · intraday halts");
        }),

        _ => Schema(dataset, "unknown", "v0.1", "—", "—", "—", root =>
            root.Object("root").Leaf("value", "—", "", "", "no schema published"))
    };

    private static DatasetSchema Schema(
        string dataset, string provider, string contract, string cadence, string coverage, string licence,
        Action<SchemaNode> build)
    {
        var root = new DataTreeNode
        {
            Name = dataset,
            Path = dataset,
            Kind = DataNodeKind.Object,
            Tag = "SUBTREE ROOT"
        };
        build(new SchemaNode(root));
        return new DatasetSchema(dataset, provider, contract, cadence, coverage, licence, root);
    }

    /// <summary>Tiny fluent builder so stub schemas stay readable — paths compose from the parent.</summary>
    private sealed class SchemaNode(DataTreeNode node)
    {
        public SchemaNode Object(string name, string tag = "")
        {
            var child = new DataTreeNode
            {
                Name = name,
                Path = $"{node.Path}.{name}",
                Kind = DataNodeKind.Object,
                Tag = tag
            };
            node.Children.Add(child);
            return new SchemaNode(child);
        }

        public SchemaNode Leaf(
            string name, string display, string sigmaDisplay, string sigmaKind, string detail, bool isVector = false)
        {
            node.Children.Add(new DataTreeNode
            {
                Name = name,
                Path = $"{node.Path}.{name}",
                Kind = DataNodeKind.Measure,
                Tag = isVector ? "VECTOR" : "",
                Reading = new Measure
                {
                    Display = display,
                    SigmaDisplay = sigmaDisplay,
                    SigmaKind = sigmaKind,
                    Detail = detail,
                    IsVector = isVector
                }
            });
            return this;
        }
    }
}
