namespace Terrafa.Continuum.Frontend.Models;

public enum DataNodeKind
{
    Object,
    Measure
}

public sealed class DataTreeNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public DataNodeKind Kind { get; init; }
    public string Tag { get; init; } = "";
    public bool IsNew { get; init; }

    /// <summary>
    /// Settable so <see cref="MeasureNumerics.BindSigmaLeaves"/> can fold a "sigma" child into its
    /// parent once the whole tree exists — a leaf cannot see its own children while it is being
    /// constructed.
    /// </summary>
    public Measure? Reading { get; set; }
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
