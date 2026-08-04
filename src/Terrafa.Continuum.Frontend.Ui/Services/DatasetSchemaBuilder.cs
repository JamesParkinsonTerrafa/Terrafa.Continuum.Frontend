// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Turns a DataFeed schema response — optionally with a block of fetched rows beside it — into the
/// tree the screens read.
///
/// <para>
/// The service speaks in Athena terms, databases holding tables of typed columns, and the app
/// speaks in datasets of measures. That mapping is this file's whole subject: a table becomes a
/// dataset, a column becomes either a leaf or, when its type is a struct, an object whose fields
/// are leaves. That last case is why the tree is worth building at all — the service resolves
/// dotted <c>parent.child</c> paths against struct fields, so a path in this tree is a path the
/// data endpoint accepts verbatim.
/// </para>
///
/// <para>
/// Every function here is pure: a response in, a tree out, no transport and no state. It was
/// previously the larger half of <see cref="HttpDatasetCatalog"/>, which made a body of logic
/// nothing to do with HTTP reachable only through an HTTP client.
/// </para>
/// </summary>
internal static class DatasetSchemaBuilder
{
    /// <param name="readings">
    /// Column path → that column's values across the ordered rows, oldest first. Null before any
    /// query has run, which is the structure-only tree.
    /// </param>
    /// <param name="maxRows">
    /// Rows kept per column, from the recent end. What arrives is whatever the service's own cap
    /// allowed; this is the client's window on top of it.
    /// </param>
    /// <param name="serviceTruncated">
    /// Whether the service reported cutting the result at its own cap. Distinct from this window:
    /// both mean rows were not seen, and the tree reports either as
    /// <see cref="DatasetSchema.Truncated"/>.
    /// </param>
    public static DatasetSchema Build(
        string dataset,
        DatasetSchemaResponse response,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        string xAxis,
        int maxRows = DataFeedOptions.SeriesRows,
        bool serviceTruncated = false)
    {
        // Measured before anything is cut, so "more arrived than we kept" is answerable.
        var arrived = readings?.Values.Select(cells => cells.Count).DefaultIfEmpty(0).Max() ?? 0;
        var root = new DataTreeNode
        {
            Name = dataset,
            Path = dataset,
            Kind = DataNodeKind.Object,
            Tag = "SUBTREE ROOT"
        };

        // A sensor_id column declares replicate members: twelve sensors reading the same
        // quantity are twelve series, and each gets its own subtree of leaves built from its own
        // rows of the one fetch.
        //
        // Repeated axis values are counted and reported — one row per axis value is what a chart
        // needs, and RowsPerPoint is how a screen says the table does not give it. Nothing is
        // dropped on account of it. The read path reports what the table holds and does not decide
        // what is fit to keep: a lookup table repeats its keys by design, and discarding its cells
        // to protect a chart nobody asked for cost the joins their data.
        var members = readings is not null ? MemberPartition(readings, maxRows) : null;
        var rowsPerPoint = 1;

        if (members is not null)
        {
            // The σ pass after this loop resolves leaves by their member-prefixed paths, so it
            // needs every member's cells in one dictionary. The keys are disjoint by
            // construction — each carries its member's name.
            var merged = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, memberReadings) in members)
            foreach (var (path, cells) in memberReadings)
                merged[path] = cells;
            readings = merged;

            foreach (var (member, memberReadings) in members)
            {
                var node = new DataTreeNode
                {
                    Name = member,
                    Path = $"{dataset}.{member}",
                    Kind = DataNodeKind.Object,
                    Tag = "SENSOR"
                };

                var memberRows = RowsPerPoint(memberReadings, $"{member}.{xAxis}");
                rowsPerPoint = Math.Max(rowsPerPoint, memberRows);
                var memberNote = memberRows > 1 ? $"{memberRows} rows/point — expected one" : "";

                foreach (var column in response.Columns ?? [])
                {
                    if (SeriesAxis.Member.Equals(column.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    Append(node, column, dataset, memberReadings, memberNote, isPartitionKey: false, depth: 0);
                }
                foreach (var column in response.PartitionKeys ?? [])
                    Append(node, column, dataset, memberReadings, memberNote, isPartitionKey: true, depth: 0);

                root.Children.Add(node);
            }
        }
        else
        {
            readings = readings is not null ? Tail(readings, maxRows) : null;
            rowsPerPoint = readings is not null ? RowsPerPoint(readings, xAxis) : 1;
            var tieNote = rowsPerPoint > 1 ? $"{rowsPerPoint} rows/point — expected one" : "";

            // Partition keys are selectable and filterable exactly like ordinary columns — Athena
            // just reports them separately — so they belong in the tree, tagged for what they are.
            foreach (var column in response.Columns ?? [])
                Append(root, column, dataset, readings, tieNote, isPartitionKey: false, depth: 0);
            foreach (var column in response.PartitionKeys ?? [])
                Append(root, column, dataset, readings, tieNote, isPartitionKey: true, depth: 0);
        }

        // Folds a "<name>__sigma" column into the measure beside it. Athena carries no uncertainty
        // of its own, so a σ column beside a reading is the only way this feed can state one. Done
        // here rather than in MeasureNumerics.BindSigmaLeaves because the pairing is row-by-row,
        // and the rows are only in hand on this side of the tree.
        BindSiblingSigma(root, dataset, readings);

        var created = response.CreatedAt;
        var accessed = response.LastAccessedAt;

        return new DatasetSchema(
            Dataset: dataset,
            Provider: response.Database ?? response.CatalogName ?? "athena",
            Contract: Humanise(response.TableType) is { Length: > 0 } type ? type : "table",
            // The service publishes no cadence or licence: neither is in an Athena catalog, so
            // there is nothing honest to put here until something upstream records them.
            Cadence: "—",
            Coverage: Coverage(created, accessed),
            Licence: "—",
            Root: root)
        {
            XAxis = xAxis,
            RowsPerPoint = rowsPerPoint,
            // Either cut counts. The service hitting its own cap and this window keeping only part
            // of what arrived both mean the same thing to anyone downstream: there is more table
            // than you are looking at.
            Truncated = serviceTruncated || arrived > maxRows,
            WindowRows = Math.Min(arrived, maxRows)
        };
    }

    /// <summary>
    /// Every measure leaf in a structure-only tree, as paths relative to the dataset. This is the
    /// full projection a read asks for before <see cref="Narrow"/> cuts it down.
    /// </summary>
    public static List<string> Leaves(DatasetSchema structure) =>
    [
        .. structure.Root
            .Descendants()
            .Where(node => node.Kind == DataNodeKind.Measure)
            .Select(node => SeriesAxis.Relative(structure.Dataset, node.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>
    /// The projection cut down to the leaves someone selected, or null to read the whole table.
    ///
    /// <para>
    /// Null is also the answer when the selection matches no column. A selection that matches
    /// nothing is stale, not an instruction to read no columns, and a read of the axis alone would
    /// blank every leaf on the screen.
    /// </para>
    /// </summary>
    public static List<string>? Narrow(
        string dataset, IReadOnlyList<string> leaves, IReadOnlyCollection<string>? wanted)
    {
        if (wanted is not { Count: > 0 }) return null;

        var relative = wanted
            .Select(path => SeriesAxis.Relative(dataset, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A member table addresses its leaves per sensor — "LIG-01.level" — while its columns stay
        // bare. The suffix match is what carries a selection across that split.
        bool Selected(string leaf) =>
            relative.Contains(leaf) ||
            relative.Any(path => path.EndsWith("." + leaf, StringComparison.OrdinalIgnoreCase));

        var kept = new List<string>();
        var matched = false;
        foreach (var leaf in leaves)
        {
            if (Selected(leaf))
            {
                kept.Add(leaf);
                matched = true;
                continue;
            }

            // Two columns nobody selects and every read needs: the one the sensors split on, and
            // the σ beside a leaf that was selected. Dropping either changes the tree's shape.
            if (leaf.Equals(SeriesAxis.Member, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(leaf);
                continue;
            }

            if (!leaf.EndsWith(MeasureNumerics.SigmaSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (Selected(leaf[..^MeasureNumerics.SigmaSuffix.Length])) kept.Add(leaf);
        }

        return matched ? kept : null;
    }

    /// <summary>
    /// The fetched rows keyed by column path, oldest first.
    ///
    /// <para>
    /// Keyed off the response's own column list, which reports the resolved path in the catalog's
    /// spelling — that, not the order we asked in, is what the values line up with. Newest first on
    /// the wire and oldest first here: a chart reads left to right, and the last point is the one
    /// the tree shows as the reading. Nothing was sorted without an axis, so there is nothing to
    /// undo — those rows stay in the order the table gave them.
    /// </para>
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string?>> ReadingsOf(
        DatasetDataResponse data, bool ordered)
    {
        var rows = data.Rows ?? [];
        var source = ordered ? rows.Reverse().ToList() : [.. rows];

        var names = data.Columns ?? [];
        var readings = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].Name is not { Length: > 0 } name) continue;
            var column = i;
            readings[name] = [.. source.Select(row => column < row.Count ? row[column] : null)];
        }
        return readings;
    }

    // ── columns ──────────────────────────────────────────────────────────────

    private static void Append(
        DataTreeNode parent,
        DatasetColumn column,
        string dataset,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        string tieNote,
        bool isPartitionKey,
        int depth)
    {
        if (string.IsNullOrWhiteSpace(column.Name)) return;

        var path = $"{parent.Path}.{column.Name}";
        var type = column.Type ?? "";

        // A struct becomes an object whose fields are addressable; every other type — including
        // arrays and maps, which a dotted path cannot enter — stays a leaf.
        if (HiveType.IsStruct(type) && depth < HiveType.MaxDepth)
        {
            var node = new DataTreeNode
            {
                Name = column.Name,
                Path = path,
                Kind = DataNodeKind.Object,
                Tag = isPartitionKey ? "PARTITION" : ""
            };
            foreach (var (fieldName, fieldType) in HiveType.StructFields(type))
                Append(node, new DatasetColumn(fieldName, fieldType, null), dataset, readings, tieNote, isPartitionKey, depth + 1);

            parent.Children.Add(node);
            return;
        }

        var isVector = HiveType.IsArray(type);
        var isBoolean = HiveType.IsBoolean(type);
        var columnPath = SeriesAxis.Relative(dataset, path);
        var cells = readings is not null && readings.TryGetValue(columnPath, out var found) ? found : [];

        // The whole transformation a value undergoes on its way to a chart: the column's non-null
        // cells, parsed, in row order. The chart plots readings by index, so a skipped null is a
        // missing measurement, not a closed gap. A boolean column reads as determinations — 1 and
        // 0 — by its declared type, not by sniffing cells. A column that reads as neither keeps
        // its text and carries no series, and the newest non-null cell is the leaf's reading
        // either way — a feed whose latest rows have not caught up still reads as its last
        // measurement. The raw cells are kept whole on the leaf regardless: History drops nulls
        // per column, so only Cells can answer for a row.
        var measured = new List<double>(cells.Count);
        string? latest = null;
        var numeric = true;
        foreach (var cell in cells)
        {
            if (cell is null) continue;
            latest = cell;
            if (!numeric) continue;
            var parsed = isBoolean
                ? MeasureNumerics.ParseBoolean(cell)
                : MeasureNumerics.ParseValue(cell).Value;
            if (double.IsNaN(parsed)) numeric = false;
            else measured.Add(parsed);
        }

        IReadOnlyList<double> history = numeric && measured.Count >= 2 ? measured : [];
        var (value, unit) = isBoolean
            ? (latest is null ? double.NaN : MeasureNumerics.ParseBoolean(latest), "")
            : MeasureNumerics.ParseValue(latest ?? "");

        parent.Children.Add(new DataTreeNode
        {
            Name = column.Name,
            Path = path,
            Kind = DataNodeKind.Measure,
            Tag = isPartitionKey ? "PARTITION" : isVector ? "VECTOR" : "",
            Reading = new Measure
            {
                Display = cells.Count > 0 ? Format(latest) : "—",
                // Athena carries no uncertainty of its own; a __sigma sibling adds one after the
                // tree is built. Leaving these blank until then is the honest reading.
                SigmaDisplay = "",
                SigmaKind = "",
                Detail = Detail(
                    type, column.Comment, isPartitionKey,
                    attempted: readings is not null,
                    sampled: cells.Count > 0,
                    points: history.Count,
                    tieNote),
                IsVector = isVector,
                IsBoolean = isBoolean,
                Value = value,
                Unit = unit,
                History = history,
                Cells = cells
            }
        });

        static string Detail(
            string type, string? comment, bool isPartitionKey,
            bool attempted, bool sampled, int points, string tieNote)
        {
            var parts = new List<string>(4);
            if (type.Length > 0) parts.Add(type);
            if (isPartitionKey) parts.Add("partition key");
            // Whether a chart will draw at all, said where the operator is already looking. A
            // column that read fine but has no series is the case that would otherwise only show
            // up as an empty tile two screens later.
            if (points > 1) parts.Add($"{points} points");
            if (tieNote.Length > 0) parts.Add(tieNote);
            if (!string.IsNullOrWhiteSpace(comment)) parts.Add(comment.Trim());
            // Only after a query actually ran does a missing value mean anything — before that it
            // just means the values have not been asked for yet. This is also what marks the
            // leaves past MaxSampleColumns, which the query deliberately left out.
            else if (attempted && !sampled) parts.Add("no sample");
            return parts.Count == 0 ? "column" : string.Join(" · ", parts);
        }
    }

    // ── members and row shape ────────────────────────────────────────────────

    /// <summary>
    /// Splits the fetched rows by <see cref="SeriesAxis.Member"/>, each member's cells keyed the
    /// way its subtree's leaves will look them up — "LIG-01.level". Null when the table has no
    /// member column, or only one member: a table already per-sensor stays flat, its sensor_id a
    /// constant leaf. One fetch, split locally: no extra queries.
    /// </summary>
    private static List<(string Member, Dictionary<string, IReadOnlyList<string?>> Readings)>? MemberPartition(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings, int maxRows)
    {
        if (!readings.TryGetValue(SeriesAxis.Member, out var sensors)) return null;

        var indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < sensors.Count; i++)
        {
            if (sensors[i] is not { Length: > 0 } sensor) continue;
            if (!indices.TryGetValue(sensor, out var rows))
            {
                rows = [];
                indices[sensor] = rows;
            }
            rows.Add(i);
        }
        if (indices.Count < 2) return null;

        var members = new List<(string, Dictionary<string, IReadOnlyList<string?>>)>(indices.Count);
        foreach (var (member, rows) in indices.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var kept = rows.Count > maxRows
                ? rows.Skip(rows.Count - maxRows).ToList()
                : rows;

            var slice = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, cells) in readings)
            {
                if (path.Equals(SeriesAxis.Member, StringComparison.OrdinalIgnoreCase)) continue;
                slice[$"{member}.{path}"] = [.. kept.Select(row => row < cells.Count ? cells[row] : null)];
            }
            members.Add((member, slice));
        }
        return members;
    }

    /// <summary>The newest <paramref name="maxRows"/> rows of each column.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string?>> Tail(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings, int maxRows)
    {
        var capped = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, cells) in readings)
        {
            capped[path] = cells.Count > maxRows
                ? [.. cells.Skip(cells.Count - maxRows)]
                : cells;
        }
        return capped;
    }

    /// <summary>Longest run of one axis value in the ordered rows — rows per point.</summary>
    private static int RowsPerPoint(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings, string xAxis)
    {
        if (!readings.TryGetValue(xAxis, out var axis) || axis.Count == 0) return 1;

        var widest = 1;
        var run = 1;
        for (var i = 1; i < axis.Count; i++)
        {
            run = axis[i] is not null && axis[i] == axis[i - 1] ? run + 1 : 1;
            if (run > widest) widest = run;
        }
        return widest;
    }

    // ── sibling σ ────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks every "&lt;name&gt;__sigma" leaf as its neighbour's σ carrier and folds its numbers
    /// into that neighbour — the flat-table spelling of what
    /// <see cref="MeasureNumerics.BindSigmaLeaves"/> does for a "sigma" child in a nested tree.
    /// σ(x) pairs row-by-row: the carrier's cell on each row the measure read from. Where the
    /// carrier is missing or unreadable on any of those rows, the flat newest σ stands alone,
    /// which <see cref="Measure.SigmaHistory"/> being empty already means.
    /// </summary>
    private static void BindSiblingSigma(
        DataTreeNode parent,
        string dataset,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings)
    {
        foreach (var node in parent.Children)
        {
            BindSiblingSigma(node, dataset, readings);

            // DeclaredReading, not Reading: this binds the tree in hand. Reading would consult the
            // store, and a second read of the same dataset would then pair a fresh measure with a
            // σ left over from the first.
            if (node.Kind != DataNodeKind.Measure || node.DeclaredReading is not { } reading) continue;
            if (reading.IsSigmaCarrier) continue;

            var carrier = parent.Children.FirstOrDefault(candidate =>
                candidate.Kind == DataNodeKind.Measure &&
                candidate.Name.Equals(node.Name + MeasureNumerics.SigmaSuffix, StringComparison.OrdinalIgnoreCase));
            if (carrier?.DeclaredReading is not { } carrierReading) continue;

            // Marked even before any values exist, so a picker never offers a standard deviation
            // as though it were a quantity — the tree keeps the leaf, sources skip it.
            carrier.Reading = carrierReading with { IsSigmaCarrier = true };

            if (!carrierReading.HasValue) continue;

            var sigmaHistory = PairedSigma(dataset, node, carrier, readings, reading.History.Count);
            var sigma = Math.Abs(sigmaHistory.Count > 0 ? sigmaHistory[^1] : carrierReading.Value);

            node.Reading = reading with
            {
                SigmaDisplay = sigma > 0 ? $"± {MeasureNumerics.FormatSigma(sigma)}" : "",
                Sigma = sigma,
                SigmaHistory = sigmaHistory
            };
        }
    }

    /// <summary>The carrier's value on each row the measure read from — or nothing.</summary>
    private static IReadOnlyList<double> PairedSigma(
        string dataset,
        DataTreeNode node,
        DataTreeNode carrier,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        int points)
    {
        if (points == 0 || readings is null) return [];
        if (!readings.TryGetValue(SeriesAxis.Relative(dataset, node.Path), out var values)) return [];
        if (!readings.TryGetValue(SeriesAxis.Relative(dataset, carrier.Path), out var sigmas)) return [];

        var paired = new List<double>(points);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is null) continue;
            if (i >= sigmas.Count || sigmas[i] is not { } cell) return [];
            var (sigma, _) = MeasureNumerics.ParseValue(cell);
            if (double.IsNaN(sigma)) return [];
            paired.Add(Math.Abs(sigma));
        }
        return paired.Count == points ? paired : [];
    }

    // ── display ──────────────────────────────────────────────────────────────

    private static string Format(string? value)
    {
        if (value is null) return "null";

        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "—";
        // The reading column is a fixed-width cell that ellipsises anyway; cutting a long struct
        // or JSON rendering here keeps one wide value from dominating the row.
        return trimmed.Length <= 40 ? trimmed : trimmed[..39] + "…";
    }

    private static string Humanise(string? tableType) =>
        (tableType ?? "").Replace('_', ' ').ToLowerInvariant();

    private static string Coverage(DateTime? created, DateTime? lastAccessed) => (created, lastAccessed) switch
    {
        ({ } from, { } to) => $"{from:yyyy-MM} → {to:yyyy-MM}",
        ({ } from, null) => $"{from:yyyy-MM} → live",
        (null, { } to) => $"… → {to:yyyy-MM}",
        _ => "—"
    };
}
