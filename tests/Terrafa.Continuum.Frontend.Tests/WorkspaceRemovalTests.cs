// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Taking a node back out of the tree. Mounting was append-only for a long time and removal had to
/// go through the whole dataset, so these pin the part that is easy to get subtly wrong: what
/// leaves alongside the node itself.
///
/// <para>
/// An ancestor is mounted to carry what was picked, never for its own sake, so it goes when it is
/// left holding nothing — and a subtree emptied that way unmounts rather than lingering as a root
/// with no leaves. The screen counts what it is about to remove from
/// <see cref="Workspace.RemovalFootprint"/>, so that is asserted against the removal itself and not
/// separately.
/// </para>
/// </summary>
[Collection("workspace")]
public class WorkspaceRemovalTests
{
    private const string Dataset = "ICE_BRENT";
    private const string Curve = $"{Dataset}.curve";
    private const string Settle = $"{Curve}.m1_settle";
    private const string Volume = $"{Curve}.m1_volume";
    private const string SecondSettle = $"{Curve}.m2_settle";
    private const string GradeSpec = $"{Dataset}.spec.grade_spec";

    private static async Task<DatasetSchema> SchemaAsync() =>
        await StubDatasetCatalog.Instance.GetSchemaAsync(Dataset);

    [Fact]
    public async Task RemovingALeafLeavesItsSiblingsAndItsParentStanding()
    {
        var schema = await SchemaAsync();
        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root);

        try
        {
            Assert.True(workspace.RemoveNode(Settle));

            Assert.Null(workspace.FindNode(Settle));
            Assert.NotNull(workspace.FindNode(Volume));
            Assert.NotNull(workspace.FindNode(Curve));
            Assert.NotNull(workspace.Find(Dataset));
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    /// <summary>
    /// The husk case. Removing leaves one at a time must not leave an empty object behind — the
    /// tree screen would draw a card for it, and nothing about it says what it is for.
    /// </summary>
    [Fact]
    public async Task AnObjectLeftHoldingNothingGoesWithItsLastLeaf()
    {
        var schema = await SchemaAsync();
        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root);

        try
        {
            workspace.RemoveNode(Settle);
            workspace.RemoveNode(Volume);
            Assert.NotNull(workspace.FindNode(Curve));

            workspace.RemoveNode(SecondSettle);

            Assert.Null(workspace.FindNode(Curve));
            // The rest of the dataset is untouched, so the subtree itself stays.
            Assert.NotNull(workspace.FindNode(GradeSpec));
            Assert.NotNull(workspace.Find(Dataset));
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    /// <summary>
    /// Removing the last of a dataset unmounts it. A root with no leaves is mounted in name only:
    /// it reports "0 leaves" on the rail and offers nothing to any screen downstream.
    /// </summary>
    [Fact]
    public async Task EmptyingASubtreeUnmountsTheDataset()
    {
        var schema = await SchemaAsync();
        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root.Find(GradeSpec)!);

        try
        {
            Assert.NotNull(workspace.Find(Dataset));

            // Two objects above the leaf — spec, which carries it, and the root itself.
            var footprint = workspace.RemovalFootprint(GradeSpec);
            Assert.Equal([Dataset, $"{Dataset}.spec", GradeSpec], footprint.Select(node => node.Path).Order());

            Assert.True(workspace.RemoveNode(GradeSpec));
            Assert.Null(workspace.Find(Dataset));
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    /// <summary>
    /// A link needs both ends. This is the part a confirm dialog has to be able to say out loud,
    /// since a link is the one thing removal takes that is not visible in the branch.
    /// </summary>
    [Fact]
    public async Task RemovingABranchSeversTheLinksHangingBeneathIt()
    {
        var schema = await SchemaAsync();
        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root);

        try
        {
            var ownLeaf = workspace.Find("SITE_ALPHA")!.Leaves.First().Path;
            var before = workspace.Links.Count;
            Assert.True(workspace.AddLink(ownLeaf, Settle, SubtreeLinkKind.Equality));

            // Cut the parent, not the linked leaf: the link hangs beneath the branch that goes.
            Assert.True(workspace.RemoveNode(Curve));

            Assert.Equal(before, workspace.Links.Count);
            Assert.DoesNotContain(workspace.Links, link => link.RightPath == Settle);
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    [Fact]
    public async Task APathThatIsNotMountedIsRefusedRatherThanGuessedAt()
    {
        var workspace = Workspace.Instance;
        Assert.False(workspace.RemoveNode($"{Dataset}.curve.m1_settle"));
        Assert.Empty(workspace.RemovalFootprint($"{Dataset}.curve.m1_settle"));

        var schema = await SchemaAsync();
        workspace.Mount(schema, schema.Root);
        try
        {
            Assert.False(workspace.RemoveNode($"{Dataset}.curve.no_such_leaf"));
            Assert.Equal(3, workspace.FindNode(Curve)!.Children.Count);
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }
}
