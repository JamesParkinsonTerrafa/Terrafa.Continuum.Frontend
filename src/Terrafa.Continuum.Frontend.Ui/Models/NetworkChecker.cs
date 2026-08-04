// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>One thing the checker objects to, addressed to the node whose card should say it.</summary>
public sealed record NetworkObjection(string NodeId, string Message);

/// <summary>
/// Composition checks that need the whole graph and the workspace, not just the two ends of a
/// wire — <see cref="NetworkGraph.CanConnect"/> refuses what can never be wired; this states what
/// is wired but cannot be answered for. Rules accrete one at a time and their findings render on
/// the cards, in the same amber the estimator's objections use.
///
/// Present rules:
///   R1 — transfers and comparators push numbers; a categorical or boolean leaf wired into one is
///        a category error to state, not evaluate around.
///   R3 — pointwise nodes zip rows by index, which only means something within one table; across
///        tables the alignment must come from a SELECT's join.
/// Owned elsewhere:
///   R2 — a SELECT over unlinked datasets states its refusal in its own note (SelectEvaluation).
///   R4 — unlike units on a comparator refuse evaluation outright (TransferMath.ComparisonObjection).
/// </summary>
public static class NetworkChecker
{
    public static IReadOnlyList<NetworkObjection> Check(NetworkGraph graph)
    {
        var objections = new List<NetworkObjection>();
        NumericInputsOnly(graph, objections);
        PointwiseStaysWithinOneTable(graph, objections);
        return objections;
    }

    /// <summary>
    /// R3. A comparator that feeds only SELECTs is spared: the select evaluates it per joined
    /// row, which is exactly the alignment the objection asks for. A transfer never is — nothing
    /// gives its zip a row order to stand on.
    /// </summary>
    private static void PointwiseStaysWithinOneTable(NetworkGraph graph, List<NetworkObjection> objections)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Kind is not (NetworkNodeKind.Transfer or NetworkNodeKind.Compare)) continue;

            var datasets = graph.InputsOf(node.Id)
                .Select(graph.Find)
                .OfType<NetworkNode>()
                .Where(input => input.Kind == NetworkNodeKind.Measure)
                .Select(input => NetworkGraph.DatasetOf(input.Key))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (datasets.Count <= 1) continue;

            if (node.Kind == NetworkNodeKind.Compare)
            {
                var outgoing = graph.Edges.Where(edge => edge.FromId == node.Id).ToList();
                var onlySelects = outgoing.Count > 0 && outgoing.All(edge =>
                    graph.Find(edge.ToId) is { Kind: NetworkNodeKind.Select });
                if (onlySelects) continue;

                objections.Add(new NetworkObjection(node.Id,
                    "spans two tables — rows only pair inside a SELECT over linked tables; wire it into one"));
            }
            else
            {
                objections.Add(new NetworkObjection(node.Id,
                    "spans two tables — series from different tables have no shared row order; select and join them instead"));
            }
        }
    }

    /// <summary>
    /// R1. Unsampled leaves stay quiet — they may yet read as numbers; a leaf whose cells are in
    /// hand and did not is categorical, and no amount of waiting changes that.
    /// </summary>
    private static void NumericInputsOnly(NetworkGraph graph, List<NetworkObjection> objections)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.Kind is not (NetworkNodeKind.Transfer or NetworkNodeKind.Compare)) continue;

            foreach (var inputId in graph.InputsOf(node.Id))
            {
                if (graph.Find(inputId) is not { Kind: NetworkNodeKind.Measure } leaf) continue;
                if (Workspace.ReadingAt(leaf.Key) is not { } reading) continue;

                var title = NetworkGraph.LeafTitle(leaf.Key);
                if (reading.IsBoolean)
                {
                    objections.Add(new NetworkObjection(node.Id, node.Kind == NetworkNodeKind.Transfer
                        ? $"'{title}' is a determination, not a quantity — a transfer cannot combine it"
                        : $"'{title}' is a determination — a comparator tests quantities"));
                }
                else if (!reading.HasValue && reading.Cells.Count > 0)
                {
                    objections.Add(new NetworkObjection(
                        node.Id, $"accepts numeric leaves only — '{title}' is categorical"));
                }
            }
        }
    }
}
