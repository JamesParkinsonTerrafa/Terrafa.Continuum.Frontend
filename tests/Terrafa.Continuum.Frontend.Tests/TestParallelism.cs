// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Xunit;

// The app's state is a handful of singletons — Workspace, ReadingStore, NetworkGraph, FigureCatalog,
// TableCatalog, Dashboard — and a test that exercises anything real mutates them. xUnit runs separate
// collections in parallel by default, so two collections that both touch the workspace were racing:
// one swapping the mounted subtrees out from under another that was indexing Subtrees[0]. It showed
// up as a failure in whichever test lost, which is why "which tests fail changes with which tests
// run" was a known property of this suite.
//
// Sequential is the honest setting while the state is global. The whole suite runs in well under a
// second, so there is nothing to buy back.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
