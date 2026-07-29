namespace Terrafa.Continuum.Frontend.Models;

/// <summary>A tile and where it sits on the canvas.</summary>
public sealed class DashboardPlacement
{
    public required DashboardTile Tile { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// The board as session state, for the same reason <see cref="NetworkGraph"/> is: mounting a dataset
/// or committing a figure on the network canvas rebuilds every screen that is not on show, and tiles
/// that only existed inside <see cref="Views.DashboardView"/> were thrown away each time. Going to
/// DATA SOURCES to add the leaf a tile needs must not cost the operator the tile.
/// </summary>
public sealed class Dashboard
{
    /// <summary>Narrower than a hand-placed tile so the seeded grid clears both side panels.</summary>
    public const double SeedWidth = 302;

    public const double SeedHeight = 252;

    public const double DefaultWidth = 360;

    public const double DefaultHeight = 250;

    public static Dashboard Instance { get; } = new();

    private readonly List<DashboardPlacement> placements = [];

    public event Action? Changed;

    public IReadOnlyList<DashboardPlacement> Placements => placements;

    public IEnumerable<DashboardTile> Tiles => placements.Select(placement => placement.Tile);

    private Dashboard() => Seed();

    public DashboardPlacement? Find(DashboardTile tile) =>
        placements.FirstOrDefault(placement => placement.Tile == tile);

    public DashboardPlacement Add(DashboardTile tile, double x, double y, double width, double height)
    {
        var placement = new DashboardPlacement
        {
            Tile = tile,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };
        placements.Add(placement);
        Changed?.Invoke();
        return placement;
    }

    public void Remove(DashboardTile tile)
    {
        if (placements.RemoveAll(placement => placement.Tile == tile) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>Geometry only — dragging a tile changes nothing another screen would want to redraw.</summary>
    public void Place(DashboardTile tile, double x, double y, double width, double height)
    {
        if (Find(tile) is not { } placement) return;
        placement.X = x;
        placement.Y = y;
        placement.Width = width;
        placement.Height = height;
    }

    public string NextName(TileKind kind)
    {
        var stem = kind switch
        {
            TileKind.Line => "line",
            TileKind.Bar => "bar",
            _ => "table"
        };
        var index = 1;
        while (Tiles.Any(tile => tile.Name == $"tile.{stem}_{index}")) index++;
        return $"tile.{stem}_{index}";
    }

    public void Reset(bool seedDemo)
    {
        placements.Clear();
        if (seedDemo) Seed();
        Changed?.Invoke();
    }

    /// <summary>
    /// A dashboard that opens empty reads as broken, so the canvas starts with worked examples of
    /// each tile form wired to real leaves and figures — including one deliberately wired to a
    /// figure that carries no σ, which is the case the master switch exists to make visible.
    /// </summary>
    private void Seed()
    {
        if (Workspace.Instance.Find("SITE_ALPHA")?.Root.Path is not { } root) return;

        Seed(TileKind.Line, "tile.tank_levels", 0, 0,
            Leaf($"{root}.tank_farm.tank_01.level"),
            Leaf($"{root}.tank_farm.tank_02.level"));

        Seed(TileKind.Table, "tile.site_readings", 1, 0,
            Leaf($"{root}.tank_farm.tank_01.level"),
            Leaf($"{root}.tank_farm.tank_01.temp"),
            Leaf($"{root}.tank_farm.tank_01.spoilage"));

        Seed(TileKind.Bar, "tile.level_compare", 2, 0,
            Leaf($"{root}.tank_farm.tank_01.level"),
            Leaf($"{root}.tank_farm.tank_02.level"));

        Seed(TileKind.Line, "tile.tank_temps", 0, 1,
            Leaf($"{root}.tank_farm.tank_01.temp"),
            Leaf($"{root}.tank_farm.tank_02.temp"));

        Seed(TileKind.Table, "tile.committed_figures", 1, 1,
            Figure("total_inventory"),
            Figure("expiry_risk"));

        Seed(TileKind.Line, "tile.log_score", 2, 1,
            Figure("log_score"));
    }

    private void Seed(TileKind kind, string name, int column, int row, params TileSource[] sources)
    {
        var tile = new DashboardTile(kind, name);
        tile.Sources.AddRange(sources);
        placements.Add(new DashboardPlacement
        {
            Tile = tile,
            X = 24 + column * (SeedWidth + 16),
            Y = 20 + row * (SeedHeight + 18),
            Width = SeedWidth,
            Height = SeedHeight
        });
    }

    private static TileSource Leaf(string path) => new(TileSourceKind.Measure, path);

    private static TileSource Figure(string key) => new(TileSourceKind.Figure, key);
}
