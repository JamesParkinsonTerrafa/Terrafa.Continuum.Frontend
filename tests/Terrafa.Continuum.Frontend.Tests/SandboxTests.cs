// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json.Nodes;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Guards the sandbox agent's one window onto the data tree: what the
/// <see cref="SandboxDataTool"/> hands back for each command, as the JSON the agent will read.
/// The catalogue is the demo stub — the interesting behaviour is the serialisation, and the demo
/// tree exercises every node kind it has to survive: structs, σ carriers, categorical leaves.
/// </summary>
public class SandboxTests
{
    [Fact]
    public async Task ListingNamesEveryTopicAndSaysWhereTheDataComesFrom()
    {
        var answer = JsonNode.Parse(await SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance, new JsonObject { ["command"] = "list_datasets" },
            CancellationToken.None))!;

        Assert.Equal("built-in demo data", answer["source"]!.GetValue<string>());
        var topics = answer["topics"]!.AsObject();
        Assert.True(topics.Count >= 6);
        var own = topics["OWN OPERATIONS"]!.AsArray().Select(name => name!.GetValue<string>());
        Assert.Contains("SITE_ALPHA", own);
    }

    [Fact]
    public async Task SchemaCarriesTheTreeWithFullLeafPaths()
    {
        var answer = JsonNode.Parse(await SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance,
            new JsonObject { ["command"] = "get_schema", ["dataset"] = "SITE_ALPHA" },
            CancellationToken.None))!;

        Assert.Equal("SITE_ALPHA", answer["dataset"]!.GetValue<string>());
        Assert.True(answer["leaf_count"]!.GetValue<int>() > 0);

        // Every measure leaf must carry the full path the agent would pass back to get_series.
        var measures = Leaves(answer["tree"]!).ToList();
        Assert.NotEmpty(measures);
        Assert.All(measures, leaf =>
            Assert.StartsWith("SITE_ALPHA.", leaf["path"]!.GetValue<string>()));
    }

    [Fact]
    public async Task SeriesAnswersWithValuesForTheAskedPathOnly()
    {
        var schema = JsonNode.Parse(await SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance,
            new JsonObject { ["command"] = "get_schema", ["dataset"] = "SITE_ALPHA" },
            CancellationToken.None))!;
        var asked = Leaves(schema["tree"]!).First()["path"]!.GetValue<string>();

        var answer = JsonNode.Parse(await SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance,
            new JsonObject
            {
                ["command"] = "get_series",
                ["dataset"] = "SITE_ALPHA",
                ["paths"] = new JsonArray(asked),
                ["max_rows"] = 16
            },
            CancellationToken.None))!;

        var series = answer["series"]!.AsObject();
        var entry = Assert.Single(series);
        Assert.Equal(asked, entry.Key);
    }

    [Fact]
    public async Task UnknownCommandsAndMissingDatasetsAreArgumentErrors()
    {
        // ArgumentException specifically: the agent loop turns these into is_error tool results,
        // and the message is what the model reads to correct itself.
        await Assert.ThrowsAsync<ArgumentException>(() => SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance, new JsonObject { ["command"] = "drop_tables" },
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => SandboxDataTool.ExecuteAsync(
            StubDatasetCatalog.Instance, new JsonObject { ["command"] = "get_schema" },
            CancellationToken.None));
    }

    [Fact]
    public void ToolDefinitionIsACompleteCustomToolDeclaration()
    {
        var definition = SandboxDataTool.Definition();
        Assert.Equal("custom", definition["type"]!.GetValue<string>());
        Assert.Equal(SandboxDataTool.Name, definition["name"]!.GetValue<string>());
        var schema = definition["input_schema"]!;
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Contains("command",
            schema["required"]!.AsArray().Select(name => name!.GetValue<string>()));
    }

    [Fact]
    public void FingerprintIsStableAndSeesEveryByte()
    {
        var config = """{"model":"claude-opus-5","system":"x"}""";
        Assert.Equal(AnthropicClient.Fingerprint(config), AnthropicClient.Fingerprint(config));
        Assert.NotEqual(AnthropicClient.Fingerprint(config),
            AnthropicClient.Fingerprint(config.Replace("x", "y")));
    }

    private static IEnumerable<JsonNode> Leaves(JsonNode node)
    {
        if (node["kind"]?.GetValue<string>() == "measure") yield return node;
        if (node["children"] is not JsonArray children) yield break;
        foreach (var child in children)
        {
            foreach (var leaf in Leaves(child!)) yield return leaf;
        }
    }
}
