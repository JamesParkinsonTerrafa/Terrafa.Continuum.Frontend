// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

public interface IDataFeed
{
    DataSnapshot Current { get; }
    event EventHandler<DataSnapshot>? SnapshotChanged;
}
