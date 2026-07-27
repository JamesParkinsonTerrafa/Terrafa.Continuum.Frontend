namespace Terrafa.Continuum.Frontend.Models;

public sealed class Measure
{
    public string Display { get; init; } = "";
    public string SigmaDisplay { get; init; } = "";
    public string SigmaKind { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Selected { get; init; }
    public bool IsNew { get; init; }
    public bool IsVector { get; init; }
}
