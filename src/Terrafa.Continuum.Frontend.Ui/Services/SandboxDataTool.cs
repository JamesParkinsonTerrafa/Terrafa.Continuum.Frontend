// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json.Nodes;
using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The sandbox agent's one window onto the data tree: a custom tool the agent calls, executed
/// here against <see cref="IDatasetCatalog"/> and answered as JSON. The agent's container never
/// holds a data-feed credential — every read comes through this bridge, through the same
/// catalogue, under the same identity, as the screens themselves.
/// </summary>
public static class SandboxDataTool
{
    public const string Name = "query_datasets";

    /// <summary>Most rows a single tool answer carries per column, however many were asked for.</summary>
    private const int MaxRows = 2000;

    /// <summary>Most leaves a series answer covers — a whole-table ask gets the first of them and says so.</summary>
    private const int MaxLeaves = 40;

    public static JsonObject Definition() => new()
    {
        ["type"] = "custom",
        ["name"] = Name,
        ["description"] =
            "Read the Terrafa Continuum data tree. Commands: list_datasets (topics and dataset " +
            "names), get_schema (one dataset's tree of objects and measure leaves, with full leaf " +
            "paths), get_series (recent readings for leaves of one dataset, ordered by the " +
            "dataset's axis column, oldest first). Always list datasets or fetch a schema before " +
            "asking for series, and pass the leaf paths you actually need — the projection is " +
            "what keeps the answer small.",
        ["input_schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("list_datasets", "get_schema", "get_series"),
                    ["description"] = "What to read."
                },
                ["dataset"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Dataset name, required for get_schema and get_series."
                },
                ["paths"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] =
                        "get_series only: full leaf paths to read, from get_schema. " +
                        "Omit to read every leaf, capped."
                },
                ["max_rows"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] =
                        $"get_series only: most recent rows to return per column. " +
                        $"Default {DataFeedOptions.SeriesRows}, cap {MaxRows}."
                }
            },
            ["required"] = new JsonArray("command")
        }
    };

    public static async Task<string> ExecuteAsync(
        IDatasetCatalog catalog, JsonNode? input, CancellationToken cancellationToken)
    {
        var command = input?["command"]?.GetValue<string>() ?? "";
        return command switch
        {
            "list_datasets" => await ListAsync(catalog, cancellationToken),
            "get_schema" => await SchemaAsync(catalog, RequireDataset(input), cancellationToken),
            "get_series" => await SeriesAsync(catalog, RequireDataset(input), input, cancellationToken),
            _ => throw new ArgumentException(
                $"unknown command '{command}' — expected list_datasets, get_schema or get_series")
        };
    }

    private static string RequireDataset(JsonNode? input) =>
        input?["dataset"]?.GetValue<string>()
        ?? throw new ArgumentException("this command needs a 'dataset'");

    private static async Task<string> ListAsync(IDatasetCatalog catalog, CancellationToken cancellationToken)
    {
        var topics = await catalog.GetAvailableDatasetsAsync(cancellationToken);
        var byTopic = new JsonObject();
        foreach (var (topic, names) in topics)
        {
            byTopic[topic] = new JsonArray([.. names.Select(name => (JsonNode)name)]);
        }

        var answer = new JsonObject
        {
            ["source"] = catalog.IsLive ? "live data feed" : "built-in demo data",
            ["topics"] = byTopic
        };
        if (catalog.Warnings.Count > 0)
        {
            answer["warnings"] = new JsonArray([.. catalog.Warnings.Select(warning => (JsonNode)warning)]);
        }
        return answer.ToJsonString();
    }

    private static async Task<string> SchemaAsync(
        IDatasetCatalog catalog, string dataset, CancellationToken cancellationToken)
    {
        var schema = await catalog.GetSchemaAsync(dataset, cancellationToken);
        return new JsonObject
        {
            ["dataset"] = schema.Dataset,
            ["provider"] = schema.Provider,
            ["contract"] = schema.Contract,
            ["cadence"] = schema.Cadence,
            ["coverage"] = schema.Coverage,
            ["licence"] = schema.Licence,
            ["axis"] = ResolveAxis(schema),
            ["leaf_count"] = schema.LeafCount,
            ["tree"] = Describe(schema.Root)
        }.ToJsonString();
    }

    private static JsonObject Describe(DataTreeNode node)
    {
        var described = new JsonObject
        {
            ["name"] = node.Name,
            ["path"] = node.Path,
            ["kind"] = node.Kind == DataNodeKind.Measure ? "measure" : "object"
        };
        if (node.Tag.Length > 0) described["tag"] = node.Tag;
        if (node.Kind == DataNodeKind.Measure && node.Reading is { } reading)
        {
            if (reading.Unit.Length > 0) described["unit"] = reading.Unit;
            if (reading.IsSigmaCarrier) described["sigma_carrier"] = true;
            if (reading.IsBoolean) described["boolean"] = true;
        }
        if (node.Children.Count > 0)
        {
            described["children"] = new JsonArray([.. node.Children.Select(Describe)]);
        }
        return described;
    }

    private static async Task<string> SeriesAsync(
        IDatasetCatalog catalog, string dataset, JsonNode? input, CancellationToken cancellationToken)
    {
        var requestedRows = input?["max_rows"]?.GetValue<int>() ?? DataFeedOptions.SeriesRows;
        var rows = Math.Clamp(requestedRows, 1, MaxRows);

        List<string>? paths = null;
        if (input?["paths"] is JsonArray asked && asked.Count > 0)
        {
            paths = [.. asked.Select(path => path?.GetValue<string>() ?? "")];
        }

        // The axis has to be settled before the read: the service sorts on it, and an unordered
        // read is not a series. The schema is the authority on which column that is.
        var schema = await catalog.GetSchemaAsync(dataset, cancellationToken);
        var axis = ResolveAxis(schema);

        var read = await catalog.GetSeriesAsync(
            new DatasetQuery(dataset, axis, paths, rows), cancellationToken);

        var leaves = read.Root.Descendants()
            .Where(node => node.Kind == DataNodeKind.Measure)
            .Where(node => paths is null || paths.Contains(node.Path))
            .ToList();
        var kept = leaves.Take(MaxLeaves).ToList();

        var series = new JsonObject();
        foreach (var leaf in kept)
        {
            if (leaf.Reading is not { } reading) continue;
            var entry = new JsonObject();
            if (reading.Unit.Length > 0) entry["unit"] = reading.Unit;
            entry["latest"] = reading.Display;
            if (reading.History.Count > 0)
            {
                entry["values"] = new JsonArray([.. Tail(reading.History, rows).Select(Number)]);
                if (reading.SigmaHistory.Count > 0)
                {
                    entry["sigma"] = new JsonArray([.. Tail(reading.SigmaHistory, rows).Select(Number)]);
                }
                else if (reading.HasVariance)
                {
                    entry["sigma_flat"] = Number(reading.Sigma);
                }
            }
            else if (reading.Cells.Count > 0)
            {
                entry["cells"] = Cells(reading.Cells, rows);
            }
            series[leaf.Path] = entry;
        }

        var answer = new JsonObject
        {
            ["dataset"] = read.Dataset,
            ["axis"] = read.XAxis.Length > 0 ? read.XAxis : axis,
            ["rows"] = read.WindowRows,
            ["series"] = series
        };

        var axisLeaf = read.Root.Descendants().FirstOrDefault(node =>
            node.Kind == DataNodeKind.Measure && SeriesAxis.Relative(read.Dataset, node.Path) == axis);
        if (axisLeaf?.Reading is { Cells.Count: > 0 } axisReading)
        {
            answer["axis_cells"] = Cells(axisReading.Cells, rows);
        }

        if (read.Truncated) answer["truncated"] = true;
        if (leaves.Count > kept.Count)
        {
            answer["note"] =
                $"{leaves.Count} leaves matched; the first {kept.Count} are here — " +
                "pass 'paths' to pick the ones you need";
        }
        return answer.ToJsonString();
    }

    private static string ResolveAxis(DatasetSchema schema) =>
        schema.XAxis.Length > 0 ? schema.XAxis : SeriesAxis.Preferred(schema) ?? SeriesAxis.Default;

    private static IEnumerable<T> Tail<T>(IReadOnlyList<T> list, int count) =>
        list.Count <= count ? list : list.Skip(list.Count - count);

    private static JsonArray Cells(IReadOnlyList<string?> cells, int rows) =>
        new([.. Tail(cells, rows).Select(cell => cell is null
            ? null
            : (JsonNode)(cell.Length > 80 ? cell[..80] : cell))]);

    /// <summary>NaN has no JSON literal — a leaf with no value says null rather than breaking the document.</summary>
    private static JsonNode? Number(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? null : JsonValue.Create(Math.Round(value, 6));
}
