// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Models;

public enum SubtreeLinkKind
{
    Equality,
    Adjacency
}

/// <summary>A cross-dataset link. Containment never crosses a dataset boundary — only these do.</summary>
public sealed class SubtreeLink
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required SubtreeLinkKind Kind { get; init; }

    public string Symbol => Kind == SubtreeLinkKind.Equality ? "≡" : "→";

    public string Label => Kind == SubtreeLinkKind.Equality ? "EQUALITY" : "ADJACENCY";
}

/// <summary>One mounted dataset. Its root mirrors the schema root, pruned to what has been added.</summary>
public sealed class MountedSubtree
{
    public required string Dataset { get; init; }
    public required DataTreeNode Root { get; init; }
    public required int AccentIndex { get; init; }
    public string Cadence { get; set; } = "";
    public string Contract { get; set; } = "";
    public bool Visible { get; set; } = true;

    /// <summary>
    /// The column the subtree's readings were ordered by when it was mounted. Empty for a subtree
    /// whose leaves carry no series. Kept so a screen downstream can say what a chart's x axis
    /// actually is rather than implying the points are evenly spaced in time.
    /// </summary>
    public string XAxis { get; set; } = "";

    public int LeafCount => Root.Descendants().Count(node => node.Kind == DataNodeKind.Measure);

    public IEnumerable<DataTreeNode> Leaves =>
        Root.Descendants().Where(node => node.Kind == DataNodeKind.Measure);
}

/// <summary>
/// Session state shared by the DATA, TREE and NETWORK screens: which dataset subtrees are
/// mounted and how their leaves are linked to each other.
/// </summary>
public sealed class Workspace
{
    public static Workspace Instance { get; } = new();

    private readonly List<MountedSubtree> subtrees = [];
    private readonly List<SubtreeLink> links = [];

    public event Action? Changed;

    public IReadOnlyList<MountedSubtree> Subtrees => subtrees;

    public IReadOnlyList<SubtreeLink> Links => links;

    public IEnumerable<MountedSubtree> VisibleSubtrees => subtrees.Where(subtree => subtree.Visible);

    private Workspace() => SeedDefaultMount();

    public bool IsMounted(string dataset) =>
        subtrees.Any(subtree => subtree.Dataset == dataset);

    public MountedSubtree? Find(string dataset) =>
        subtrees.FirstOrDefault(subtree => subtree.Dataset == dataset);

    public DataTreeNode? FindNode(string path)
    {
        foreach (var subtree in subtrees)
        {
            var match = subtree.Root.Find(path);
            if (match is not null) return match;
        }
        return null;
    }

    public MountedSubtree? SubtreeOf(string path) =>
        subtrees.FirstOrDefault(subtree => subtree.Root.Find(path) is not null);

    /// <summary>
    /// Grafts <paramref name="node"/> — and everything beneath it — onto the dataset's mounted
    /// subtree, recreating the ancestor chain so the branch keeps its shape.
    /// </summary>
    public MountedSubtree Mount(DatasetSchema schema, DataTreeNode node)
    {
        var subtree = Find(schema.Dataset) ?? CreateSubtree(schema);
        subtree.Cadence = schema.Cadence;
        subtree.Contract = schema.Contract;
        if (schema.XAxis.Length > 0) subtree.XAxis = schema.XAxis;

        var chain = PathTo(schema.Root, node) ?? [node];
        var cursor = subtree.Root;
        for (var i = 1; i < chain.Count - 1; i++)
            cursor = EnsureChild(cursor, chain[i]);

        if (chain.Count > 1) Graft(cursor, node);
        else foreach (var child in node.Children) Graft(cursor, child);

        Changed?.Invoke();
        return subtree;
    }

    /// <summary>
    /// Drops <paramref name="path"/> and everything beneath it. An object left holding nothing goes
    /// with it — ancestors are mounted to carry what was picked, never for their own sake — and a
    /// subtree emptied to its root unmounts, a dataset with no leaves being mounted in name only.
    /// Cross-subtree links to anything that left go too: a link needs both ends.
    /// </summary>
    public bool RemoveNode(string path)
    {
        if (Cut(path) is not { } cut) return false;

        if (ReferenceEquals(cut.Top, cut.Subtree.Root))
        {
            Unmount(cut.Subtree.Dataset);
            return true;
        }

        cut.Parent!.Children.Remove(cut.Top);
        links.RemoveAll(link => FindNode(link.LeftPath) is null || FindNode(link.RightPath) is null);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The nodes <see cref="RemoveNode"/> would take, so a screen can say what it is about to do and
    /// have that be what happens. Empty when the path is not mounted. Includes the subtree root when
    /// the removal empties it, which is the shape of "this unmounts the dataset".
    /// </summary>
    public IReadOnlyList<DataTreeNode> RemovalFootprint(string path) =>
        Cut(path) is { } cut ? [cut.Top, .. cut.Top.Descendants()] : [];

    /// <summary>
    /// Where the branch is severed: the highest ancestor that would be left holding nothing but the
    /// node, or the node itself when its parent holds more than it. Everything under that one cut is
    /// exactly what leaves — a parent is only followed upwards while it has a single child, so
    /// nothing with a sibling is ever swept up.
    /// </summary>
    private (MountedSubtree Subtree, DataTreeNode Top, DataTreeNode? Parent)? Cut(string path)
    {
        if (SubtreeOf(path) is not { } subtree) return null;
        if (subtree.Root.Find(path) is not { } node) return null;
        if (PathTo(subtree.Root, node) is not { } chain) return null;

        var index = chain.Count - 1;
        while (index > 0 && chain[index - 1].Children.Count == 1) index--;
        return (subtree, chain[index], index > 0 ? chain[index - 1] : null);
    }

    public void Unmount(string dataset)
    {
        var subtree = Find(dataset);
        if (subtree is null) return;
        subtrees.Remove(subtree);
        links.RemoveAll(link =>
            link.LeftPath.StartsWith(dataset + ".", StringComparison.Ordinal) ||
            link.RightPath.StartsWith(dataset + ".", StringComparison.Ordinal));
        Changed?.Invoke();
    }

    public void SetVisible(MountedSubtree subtree, bool visible)
    {
        if (subtree.Visible == visible) return;
        subtree.Visible = visible;
        Changed?.Invoke();
    }

    public bool AddLink(string leftPath, string rightPath, SubtreeLinkKind kind)
    {
        if (leftPath == rightPath) return false;
        if (SubtreeOf(leftPath) is not { } left || SubtreeOf(rightPath) is not { } right) return false;
        if (left == right) return false;
        if (links.Any(link => Same(link, leftPath, rightPath))) return false;

        links.Add(new SubtreeLink { LeftPath = leftPath, RightPath = rightPath, Kind = kind });
        Changed?.Invoke();
        return true;
    }

    public void RemoveLink(SubtreeLink link)
    {
        if (!links.Remove(link)) return;
        Changed?.Invoke();
    }

    public int CountLinks(SubtreeLinkKind kind) => links.Count(link => link.Kind == kind);

    private static bool Same(SubtreeLink link, string leftPath, string rightPath) =>
        (link.LeftPath == leftPath && link.RightPath == rightPath) ||
        (link.LeftPath == rightPath && link.RightPath == leftPath);

    private MountedSubtree CreateSubtree(DatasetSchema schema)
    {
        var subtree = new MountedSubtree
        {
            Dataset = schema.Dataset,
            AccentIndex = subtrees.Count,
            Root = new DataTreeNode
            {
                Name = schema.Root.Name,
                Path = schema.Root.Path,
                Kind = DataNodeKind.Object,
                Tag = "SUBTREE ROOT"
            }
        };
        subtrees.Add(subtree);
        return subtree;
    }

    /// <summary>Root-to-node chain, or null when the node is not part of that schema.</summary>
    private static List<DataTreeNode>? PathTo(DataTreeNode root, DataTreeNode target)
    {
        if (ReferenceEquals(root, target)) return [root];
        foreach (var child in root.Children)
        {
            if (PathTo(child, target) is not { } tail) continue;
            tail.Insert(0, root);
            return tail;
        }
        return null;
    }

    private static DataTreeNode EnsureChild(DataTreeNode parent, DataTreeNode source)
    {
        var existing = parent.Children.FirstOrDefault(child => child.Path == source.Path);
        if (existing is not null) return existing;

        var created = Clone(source, withChildren: false);
        parent.Children.Add(created);
        return created;
    }

    private static void Graft(DataTreeNode parent, DataTreeNode source)
    {
        var existing = parent.Children.FirstOrDefault(child => child.Path == source.Path);
        if (existing is null)
        {
            parent.Children.Add(Clone(source, withChildren: true));
            return;
        }
        // Re-adding a node the mount already holds refreshes what is behind it: the newest read
        // wins. Without this, a subtree mounted while its first fetch was still in flight kept
        // its empty readings forever, and re-adding it did nothing.
        if (source.Reading is not null) existing.Reading = source.Reading;
        foreach (var child in source.Children)
            Graft(existing, child);
    }

    private static DataTreeNode Clone(DataTreeNode source, bool withChildren)
    {
        var copy = new DataTreeNode
        {
            Name = source.Name,
            Path = source.Path,
            Kind = source.Kind,
            Tag = source.Tag,
            IsNew = source.IsNew,
            Reading = source.Reading
        };
        if (withChildren)
        {
            foreach (var child in source.Children)
                copy.Children.Add(Clone(child, withChildren: true));
        }
        return copy;
    }

    /// <summary>
    /// Writes a fresh read's values onto an already-mounted subtree, node by path. Mounting is
    /// deliberately shape-preserving — it never adds leaves the operator did not pick — but the
    /// values behind the picked leaves belong to the newest read: a dataset mounted while its
    /// first fetch was still in flight would otherwise sit valueless until someone unmounted it.
    /// </summary>
    public void RefreshReadings(DatasetSchema schema)
    {
        if (Find(schema.Dataset) is not { } subtree) return;

        var refreshed = false;
        foreach (var node in subtree.Root.Descendants())
        {
            if (schema.Root.Find(node.Path)?.Reading is not { } fresh) continue;
            node.Reading = fresh;
            refreshed = true;
        }

        if (!refreshed) return;
        if (schema.XAxis.Length > 0) subtree.XAxis = schema.XAxis;
        Changed?.Invoke();
    }

    /// <summary>
    /// Empties the workspace, optionally re-seeding the demo mount. Called when the session
    /// changes: a subtree mounted from the demo catalogue must not outlive it, or the tree and
    /// network screens keep drawing leaves whose dataset is no longer listed anywhere.
    /// </summary>
    public void Reset(bool seedDemo)
    {
        subtrees.Clear();
        links.Clear();
        if (seedDemo) SeedDefaultMount();
        Changed?.Invoke();
    }

    /// <summary>The site the operator owns is mounted up front — every other dataset is opt-in.</summary>
    private void SeedDefaultMount() => Mount(StubDatasetCatalog.SiteAlpha, StubDatasetCatalog.SiteAlpha.Root);
}
