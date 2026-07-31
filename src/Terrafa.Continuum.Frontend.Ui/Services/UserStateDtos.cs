// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json.Serialization;

namespace Terrafa.Continuum.Frontend.Services;

// The wire shapes of the per-user durable state, one document per kind. Every root carries a
// schema version, and every field is nullable so a document written by an older build applies the
// fields it has and leaves the rest at their defaults.

public static class UserStateKinds
{
    public const string Settings = "settings";
    public const string Dashboard = "dashboard";
    public const string Functions = "functions";
    public const string Workspace = "workspace";
    public const string Network = "network";

    public const int SchemaVersion = 1;

    public static IReadOnlyList<string> All { get; } =
        [Settings, Dashboard, Functions, Workspace, Network];
}

public sealed record SettingsState(
    int SchemaVersion,
    bool? IsLight,
    double? UiScale,
    double? TypographyScale,
    bool? SnapEnabled,
    bool? ShowGridLines,
    bool? HintsEnabled,
    bool? BuilderMode,
    bool? VarianceEnabled,
    IReadOnlyList<int>? NavOrder,
    double? NodeSaturation,
    double? NodeCornerRadius,
    double? HighlightSaturation,
    double? HighlightBrightness,
    double? ButtonEmbossStrength,
    double? ButtonCornerRadius,
    double? BubblePopSpeed,
    double? BubblePopForce,
    double? BubbleWobble,
    double? BubbleHoldSeconds,
    double? GrainIntensity,
    int? GrainBaseWavelength,
    double? GrainSpectralSlope,
    double? GrainWarpStrength,
    double? GrainFineGrain,
    bool? TabsVertical = null);

public sealed record DashboardState(int SchemaVersion, IReadOnlyList<TileState>? Tiles);

public sealed record TileState(
    string? Name,
    string? Kind,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<TileSourceState>? Sources);

public sealed record TileSourceState(string? Kind, string? Path, string? SigmaFigureKey);

public sealed record FunctionsState(int SchemaVersion, IReadOnlyList<UserFunctionState>? Functions);

public sealed record UserFunctionState(string? Name, CompositionNodeState? Definition);

/// <summary>
/// A composition tree node, discriminated by <see cref="Type"/> ("var", "const" or "fn") rather
/// than a polymorphic hierarchy — source-generated serialisation handles one concrete shape
/// without converters. A function node stores the library function's name only; the definition is
/// resolved against <see cref="Models.FunctionLibrary"/> on load.
/// </summary>
public sealed record CompositionNodeState(
    string? Type,
    double? Value,
    string? Function,
    IReadOnlyList<CompositionNodeState>? Arguments);

public sealed record WorkspaceState(
    int SchemaVersion,
    IReadOnlyList<MountState>? Mounts,
    IReadOnlyList<LinkState>? Links);

/// <param name="LeafPaths">
/// The measure paths the operator mounted. The tree's shape is not persisted — it comes from the
/// dataset's schema, and re-mounting each leaf recreates the pruned ancestor chains.
/// </param>
public sealed record MountState(
    string? Dataset,
    string? XAxis,
    bool Visible,
    IReadOnlyList<string>? LeafPaths);

public sealed record LinkState(string? LeftPath, string? RightPath, string? Kind);

public sealed record NetworkState(
    int SchemaVersion,
    IReadOnlyList<NetworkNodeState>? Nodes,
    IReadOnlyList<NetworkEdgeState>? Edges);

public sealed record NetworkNodeState(
    string? Id,
    string? Kind,
    string? Key,
    double X,
    double Y,
    string? Combiner,
    string? Stage,
    string? Estimator,
    bool IsOpaque,
    string? OpaqueTitle);

public sealed record NetworkEdgeState(string? FromId, string? ToId, string? Port);

/// <summary>
/// Source-generated metadata, for the same reason <see cref="DataFeedJson"/> is: the browser head
/// publishes with AOT and the trimmer, which strips reflection-based serialisation.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SettingsState))]
[JsonSerializable(typeof(DashboardState))]
[JsonSerializable(typeof(FunctionsState))]
[JsonSerializable(typeof(WorkspaceState))]
[JsonSerializable(typeof(NetworkState))]
public sealed partial class UserStateJson : JsonSerializerContext;
