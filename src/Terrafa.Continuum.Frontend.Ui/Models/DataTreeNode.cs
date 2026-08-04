// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

public enum DataNodeKind
{
    Object,
    Measure
}

public sealed class DataTreeNode
{
    private Measure? declared;

    public required string Name { get; init; }
    public required string Path { get; init; }
    public DataNodeKind Kind { get; init; }
    public string Tag { get; init; } = "";
    public bool IsNew { get; init; }

    /// <summary>
    /// The value behind this leaf.
    ///
    /// <para>
    /// The getter asks <see cref="ReadingStore"/> first, so a node that was copied into a mount
    /// still reports the newest read rather than the one it was built with. Nothing has to walk the
    /// tree writing values in, and no copy of a node can go stale.
    /// </para>
    ///
    /// <para>
    /// The setter writes what the tree itself declares, which is what the demo data carries and
    /// what a fetched schema is built with. It is settable so
    /// <see cref="MeasureNumerics.BindSigmaLeaves"/> can fold a "sigma" child into its parent once
    /// the whole tree exists — a leaf cannot see its own children while it is being constructed.
    /// </para>
    /// </summary>
    public Measure? Reading
    {
        get => ReadingStore.Instance.Find(Path) ?? declared;
        set => declared = value;
    }

    /// <summary>
    /// What this node was built with, before the store is consulted. Schema construction reads it
    /// rather than <see cref="Reading"/>: binding a σ carrier or naming an axis must work off the
    /// tree in hand, not off values a previous read left behind.
    /// </summary>
    public Measure? DeclaredReading => declared;

    public List<DataTreeNode> Children { get; } = [];

    public string KindLabel => Kind == DataNodeKind.Object
        ? (Tag.Length > 0 ? $"OBJECT · {Tag}" : "OBJECT")
        : (Tag.Length > 0 ? $"MEASURE · {Tag}" : "MEASURE");

    public IEnumerable<DataTreeNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var grandChild in child.Descendants())
                yield return grandChild;
        }
    }

    public DataTreeNode? Find(string path)
    {
        if (Path == path) return this;
        foreach (var child in Children)
        {
            var match = child.Find(path);
            if (match is not null) return match;
        }
        return null;
    }
}
