using System.Reflection;
using System.Text;

namespace Terrafa.Continuum.Frontend.Models;

public static class DataTreeBuilder
{
    public static DataTreeNode Build(object source, string rootName, string rootTag = "ROOT")
    {
        var root = new DataTreeNode
        {
            Name = rootName,
            Path = rootName,
            Kind = DataNodeKind.Object,
            Tag = rootTag
        };
        AppendChildren(source, root);
        MeasureNumerics.BindSigmaLeaves(root);
        return root;
    }

    private static void AppendChildren(object source, DataTreeNode parent)
    {
        foreach (var property in source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = property.GetValue(source);
            if (value is null) continue;

            var name = property.GetCustomAttribute<TreeNameAttribute>()?.Name ?? ToSnakeCase(property.Name);
            var tag = property.GetCustomAttribute<TreeTagAttribute>()?.Tag ?? "";
            var isNew = property.GetCustomAttribute<TreeNewAttribute>() is not null;
            var path = $"{parent.Path}.{name}";

            if (value is Measure reading)
            {
                parent.Children.Add(new DataTreeNode
                {
                    Name = name,
                    Path = path,
                    Kind = DataNodeKind.Measure,
                    Tag = tag.Length > 0 ? tag : (reading.IsVector ? "VECTOR" : ""),
                    IsNew = isNew || reading.IsNew,
                    Reading = MeasureNumerics.Hydrate(reading, path)
                });
                continue;
            }

            if (IsNestedObject(property.PropertyType))
            {
                var node = new DataTreeNode
                {
                    Name = name,
                    Path = path,
                    Kind = DataNodeKind.Object,
                    Tag = tag,
                    IsNew = isNew
                };
                AppendChildren(value, node);
                parent.Children.Add(node);
            }
        }
    }

    private static bool IsNestedObject(Type type) =>
        type.IsClass && type != typeof(string) && !type.IsArray;

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            var startsNewWord = i > 0 &&
                (char.IsUpper(current) || (char.IsDigit(current) && !char.IsDigit(value[i - 1])));
            if (startsNewWord) builder.Append('_');
            builder.Append(char.ToLowerInvariant(current));
        }
        return builder.ToString();
    }
}
