namespace Terrafa.Continuum.Frontend.Services;

public interface IDataFeed
{
    DataSnapshot Current { get; }
    event EventHandler<DataSnapshot>? SnapshotChanged;
}
