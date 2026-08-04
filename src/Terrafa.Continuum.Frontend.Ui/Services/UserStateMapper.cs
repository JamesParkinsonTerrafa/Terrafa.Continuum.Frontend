// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Between the live singletons and their wire shapes. Capture reads current state; Apply writes a
/// loaded document back, tolerating anything missing — a field an older build never wrote keeps
/// its default, a function or leaf that no longer exists is skipped rather than thrown on.
/// </summary>
public static class UserStateMapper
{
    /// <summary>Composition trees past this depth are refused on load — nothing legitimate is
    /// this deep, and a hostile document must not recurse the client into a stack overflow.</summary>
    private const int MaxCompositionDepth = 64;

    // ── settings ─────────────────────────────────────────────────────────────

    public static SettingsState CaptureSettings() => new(
        UserStateKinds.SchemaVersion,
        ThemeManager.IsLight,
        UiScaleSettings.Scale,
        TypographySettings.Scale,
        SnapSettings.Enabled,
        SnapSettings.ShowGridLines,
        HintSettings.Enabled,
        BuilderModeSettings.Enabled,
        VarianceSettings.Enabled,
        NavOrderSettings.OrderFor(NavOrderSettings.Default.Count),
        AppearanceSettings.NodeSaturation,
        AppearanceSettings.NodeCornerRadius,
        AppearanceSettings.HighlightSaturation,
        AppearanceSettings.HighlightBrightness,
        ButtonSettings.IdleEmbossStrength,
        ButtonSettings.CornerRadius,
        BubbleSettings.PopSpeed,
        BubbleSettings.PopForce,
        BubbleSettings.Wobble,
        BubbleSettings.HoldSeconds,
        GrainSettings.Intensity,
        GrainSettings.BaseWavelength,
        GrainSettings.SpectralSlope,
        GrainSettings.WarpStrength,
        GrainSettings.FineGrain,
        TabLayoutSettings.Vertical,
        TableCacheSettings.CacheRows,
        TableCacheSettings.EvictionRows);

    public static void ApplySettings(SettingsState state)
    {
        // Every setter clamps and no-ops on an unchanged value, so applying is just calling them.
        if (state.IsLight is { } isLight) ThemeManager.SetLight(isLight);
        if (state.UiScale is { } uiScale) UiScaleSettings.SetScale(uiScale);
        if (state.TypographyScale is { } typography) TypographySettings.SetScale(typography);
        if (state.SnapEnabled is { } snap) SnapSettings.SetEnabled(snap);
        if (state.ShowGridLines is { } gridLines) SnapSettings.SetShowGridLines(gridLines);
        if (state.HintsEnabled is { } hints) HintSettings.SetEnabled(hints);
        if (state.BuilderMode is { } builder) BuilderModeSettings.SetEnabled(builder);
        if (state.VarianceEnabled is { } variance) VarianceSettings.SetEnabled(variance);
        if (state.NavOrder is { } navOrder) NavOrderSettings.Set(navOrder);
        if (state.NodeSaturation is { } nodeSaturation) AppearanceSettings.SetNodeSaturation(nodeSaturation);
        if (state.NodeCornerRadius is { } nodeCorner) AppearanceSettings.SetNodeCornerRadius(nodeCorner);
        if (state.HighlightSaturation is { } highlightSat) AppearanceSettings.SetHighlightSaturation(highlightSat);
        if (state.HighlightBrightness is { } highlightBright) AppearanceSettings.SetHighlightBrightness(highlightBright);
        if (state.ButtonEmbossStrength is { } emboss) ButtonSettings.SetIdleEmbossStrength(emboss);
        if (state.ButtonCornerRadius is { } buttonCorner) ButtonSettings.SetCornerRadius(buttonCorner);
        if (state.BubblePopSpeed is { } popSpeed) BubbleSettings.SetPopSpeed(popSpeed);
        if (state.BubblePopForce is { } popForce) BubbleSettings.SetPopForce(popForce);
        if (state.BubbleWobble is { } wobble) BubbleSettings.SetWobble(wobble);
        if (state.BubbleHoldSeconds is { } hold) BubbleSettings.SetHoldSeconds(hold);
        if (state.GrainIntensity is { } grain) GrainSettings.SetIntensity(grain);
        if (state.GrainBaseWavelength is { } wavelength) GrainSettings.SetBaseWavelength(wavelength);
        if (state.GrainSpectralSlope is { } slope) GrainSettings.SetSpectralSlope(slope);
        if (state.GrainWarpStrength is { } warp) GrainSettings.SetWarpStrength(warp);
        if (state.GrainFineGrain is { } fine) GrainSettings.SetFineGrain(fine);
        if (state.TabsVertical is { } tabsVertical) TabLayoutSettings.SetVertical(tabsVertical);
        if (state.TableCacheRows is { } cacheRows) TableCacheSettings.SetCacheRows(cacheRows);
        if (state.TableEvictionRows is { } evictionRows) TableCacheSettings.SetEvictionRows(evictionRows);
    }

    // ── dashboard ────────────────────────────────────────────────────────────

    public static DashboardState CaptureDashboard() => new(
        UserStateKinds.SchemaVersion,
        [
            .. Dashboard.Instance.Placements.Select(placement => new TileState(
                placement.Tile.Name,
                placement.Tile.Kind.ToString(),
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                [
                    .. placement.Tile.Sources.Select(source => new TileSourceState(
                        source.Kind.ToString(), source.Path, source.SigmaFigureKey))
                ],
                placement.Tile.IndexLeaf.Length > 0 ? placement.Tile.IndexLeaf : null,
                placement.Tile.HighlightBooleans))
        ]);

    public static void ApplyDashboard(DashboardState state)
    {
        var placements = new List<DashboardPlacement>();
        foreach (var tileState in state.Tiles ?? [])
        {
            if (!Enum.TryParse<TileKind>(tileState.Kind, out var kind)) continue;
            var tile = new DashboardTile(kind, tileState.Name ?? "")
            {
                IndexLeaf = tileState.IndexLeaf ?? "",
                HighlightBooleans = tileState.HighlightBooleans
            };
            foreach (var source in tileState.Sources ?? [])
            {
                if (source.Path is not { Length: > 0 } path) continue;
                if (!Enum.TryParse<TileSourceKind>(source.Kind, out var sourceKind)) continue;
                tile.Sources.Add(new TileSource(sourceKind, path, source.SigmaFigureKey));
            }
            placements.Add(new DashboardPlacement
            {
                Tile = tile,
                X = tileState.X,
                Y = tileState.Y,
                Width = Math.Max(tileState.Width, Dashboard.MinTileWidth),
                Height = Math.Max(tileState.Height, Dashboard.MinTileHeight)
            });
        }
        Dashboard.Instance.Load(placements);
    }

    // ── transfer functions ───────────────────────────────────────────────────

    public static FunctionsState CaptureFunctions() => new(
        UserStateKinds.SchemaVersion,
        [
            .. FunctionLibrary.Instance.UserFunctions
                .Where(function => function.Definition is not null)
                .Select(function => new UserFunctionState(function.Name, ToState(function.Definition!)))
        ]);

    /// <summary>
    /// Rebuilds the saved composites. A composite may reference another one, so unresolved
    /// definitions are retried after each pass lands a new name; whatever still cannot resolve —
    /// a function renamed away, a corrupt node — is dropped rather than blocking the rest.
    /// </summary>
    public static void ApplyFunctions(FunctionsState state)
    {
        var pending = (state.Functions ?? [])
            .Where(function => function.Name is { Length: > 0 } && function.Definition is not null)
            .ToList();
        var built = new List<LibraryFunction>();
        var byName = new Dictionary<string, LibraryFunction>(StringComparer.Ordinal);

        var progressed = true;
        while (progressed && pending.Count > 0)
        {
            progressed = false;
            for (var index = 0; index < pending.Count;)
            {
                var candidate = pending[index];
                if (FromState(candidate.Definition!, byName, depth: 0) is { } root)
                {
                    var composite = FunctionLibrary.BuildComposite(candidate.Name!, root);
                    built.Add(composite);
                    byName[candidate.Name!] = composite;
                    pending.RemoveAt(index);
                    progressed = true;
                }
                else
                {
                    index++;
                }
            }
        }

        FunctionLibrary.Instance.LoadUserFunctions(built);
    }

    public static CompositionNodeState ToState(CompositionNode node) => node switch
    {
        VariableNode => new CompositionNodeState("var", null, null, null),
        ConstantNode constant => new CompositionNodeState("const", constant.Value, null, null),
        FunctionNode function => new CompositionNodeState(
            "fn", null, function.Function.Name, [.. function.Arguments.Select(ToState)]),
        _ => new CompositionNodeState("var", null, null, null)
    };

    /// <summary>Null when anything in the tree does not resolve — the caller may retry later.</summary>
    public static CompositionNode? FromState(
        CompositionNodeState state, IReadOnlyDictionary<string, LibraryFunction> userFunctions, int depth)
    {
        if (depth > MaxCompositionDepth) return null;
        switch (state.Type)
        {
            case "var":
                return new VariableNode();
            case "const":
                return state.Value is { } value ? new ConstantNode(value) : null;
            case "fn":
            {
                if (state.Function is not { Length: > 0 } name) return null;
                var function =
                    FunctionLibrary.Instance.Primitives.FirstOrDefault(primitive => primitive.Name == name)
                    ?? userFunctions.GetValueOrDefault(name);
                if (function is null) return null;

                var arguments = new List<CompositionNode>();
                foreach (var argumentState in state.Arguments ?? [])
                {
                    if (FromState(argumentState, userFunctions, depth + 1) is not { } argument) return null;
                    arguments.Add(argument);
                }
                try
                {
                    return new FunctionNode(function, arguments);
                }
                catch (ArgumentException)
                {
                    // Wrong arity — the primitive's signature moved since this was saved.
                    return null;
                }
            }
            default:
                return null;
        }
    }

    // ── workspace ────────────────────────────────────────────────────────────

    public static WorkspaceState CaptureWorkspace() => new(
        UserStateKinds.SchemaVersion,
        [
            .. Workspace.Instance.Subtrees.Select(subtree => new MountState(
                subtree.Dataset,
                subtree.XAxis,
                subtree.Visible,
                [.. subtree.Leaves.Select(leaf => leaf.Path)]))
        ],
        [
            .. Workspace.Instance.Links.Select(link => new LinkState(
                link.LeftPath, link.RightPath, link.Kind.ToString()))
        ]);

    /// <summary>
    /// Rebuilds each saved dataset's subtree from its schema, leaf by saved leaf, then swaps the
    /// whole set in at once.
    ///
    /// <para>
    /// The build happens to one side and the swap happens at the end. Emptying the workspace first
    /// is what turned an unreachable service into an empty screen: the tree went, nothing replaced
    /// it, and every tile wired to it reported its source missing. A restore that mounts nothing
    /// now leaves the tree it found in place.
    /// </para>
    ///
    /// <para>
    /// This restores structure only. The values behind the leaves are read afterwards by
    /// <see cref="ReadingLoader"/>, against the selection this rebuilt.
    /// </para>
    /// </summary>
    public static async Task ApplyWorkspaceAsync(WorkspaceState state, IDatasetCatalog catalog)
    {
        var rebuilt = new List<MountedSubtree>();

        // The whole-restore promise, held per dataset: a saved mount the catalogue cannot rebuild
        // keeps whatever this machine already has for it. The demo site is the standing case — it
        // is seeded on every machine and served by no live catalogue, so a signed-in restore that
        // rebuilt any live dataset used to swap it away and kill every tile wired to it.
        void KeepExisting(string dataset)
        {
            if (Workspace.Instance.Find(dataset) is { } existing) rebuilt.Add(existing);
        }

        foreach (var mount in state.Mounts ?? [])
        {
            if (mount.Dataset is not { Length: > 0 } dataset) continue;

            DatasetSchema schema;
            try
            {
                schema = await catalog.GetSchemaAsync(dataset);
            }
            catch (Exception)
            {
                KeepExisting(dataset);
                continue;
            }

            var subtree = Workspace.NewSubtree(schema, rebuilt.Count);
            var mounted = false;
            foreach (var path in mount.LeafPaths ?? [])
            {
                if (schema.Root.Find(path) is not { } node) continue;
                Workspace.GraftInto(subtree, schema, node);
                mounted = true;
            }
            if (!mounted)
            {
                KeepExisting(dataset);
                continue;
            }

            subtree.Visible = mount.Visible;
            if (mount.XAxis is { Length: > 0 } axis) subtree.XAxis = axis;
            rebuilt.Add(subtree);
        }

        var links = new List<SubtreeLink>();
        foreach (var link in state.Links ?? [])
        {
            if (link.LeftPath is not { Length: > 0 } left || link.RightPath is not { Length: > 0 } right) continue;
            if (!Enum.TryParse<SubtreeLinkKind>(link.Kind, out var kind)) continue;
            if (left == right) continue;
            if (rebuilt.FirstOrDefault(subtree => subtree.Root.Find(left) is not null) is not { } leftSubtree) continue;
            if (rebuilt.FirstOrDefault(subtree => subtree.Root.Find(right) is not null) is not { } rightSubtree) continue;
            if (leftSubtree == rightSubtree) continue;

            links.Add(new SubtreeLink { LeftPath = left, RightPath = right, Kind = kind });
        }

        Workspace.Instance.Swap(rebuilt, links);
    }

    // ── network ──────────────────────────────────────────────────────────────

    public static NetworkState CaptureNetwork() => new(
        UserStateKinds.SchemaVersion,
        [
            .. NetworkGraph.Instance.Nodes.Select(node => new NetworkNodeState(
                node.Id,
                node.Kind.ToString(),
                node.Key,
                node.X,
                node.Y,
                node.Combiner.ToString(),
                node.Stage,
                node.Estimator,
                node.IsOpaque,
                node.OpaqueTitle,
                node.Operator.Length > 0 ? node.Operator : null))
        ],
        [
            .. NetworkGraph.Instance.Edges.Select(edge => new NetworkEdgeState(
                edge.FromId, edge.ToId, edge.Port))
        ]);

    public static void ApplyNetwork(NetworkState state)
    {
        var nodes = new List<NetworkNode>();
        foreach (var nodeState in state.Nodes ?? [])
        {
            if (nodeState.Id is not { Length: > 0 } id) continue;
            if (!Enum.TryParse<NetworkNodeKind>(nodeState.Kind, out var kind)) continue;
            Enum.TryParse<TransferCombiner>(nodeState.Combiner, out var combiner);
            nodes.Add(new NetworkNode
            {
                Id = id,
                Kind = kind,
                Key = nodeState.Key ?? "",
                X = nodeState.X,
                Y = nodeState.Y,
                Combiner = combiner,
                Stage = nodeState.Stage ?? "",
                Estimator = nodeState.Estimator ?? "",
                IsOpaque = nodeState.IsOpaque,
                OpaqueTitle = nodeState.OpaqueTitle ?? "",
                Operator = nodeState.Operator ?? ""
            });
        }

        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = (state.Edges ?? [])
            .Where(edge => edge.FromId is { Length: > 0 } && edge.ToId is { Length: > 0 })
            .Where(edge => ids.Contains(edge.FromId!) && ids.Contains(edge.ToId!))
            .Select(edge => new NetworkEdge(edge.FromId!, edge.ToId!, edge.Port ?? ""))
            .ToList();

        NetworkGraph.Instance.Load(nodes, edges);
    }
}
