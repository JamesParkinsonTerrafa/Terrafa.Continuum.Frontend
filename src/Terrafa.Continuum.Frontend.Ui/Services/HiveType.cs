// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The little bit of Hive type-string reading the catalogue tree needs.
///
/// <para>
/// Only <c>struct&lt;...&gt;</c> is walked, and that is not an arbitrary choice: the service resolves
/// a caller's dotted <c>parent.child</c> column path by stepping through struct fields and nothing
/// else, so a struct is precisely the type that can become an object node whose leaves are still
/// addressable. An array or a map needs an index or a key the API has no syntax for, so those stay
/// leaves — expanding them would put paths in the tree that the data endpoint would reject.
/// </para>
/// </summary>
internal static class HiveType
{
    private const string StructPrefix = "struct<";

    /// <summary>
    /// How deep a struct may nest before the walk stops. Athena's own limit is far below this;
    /// the cap is here so a malformed type string cannot recurse without end.
    /// </summary>
    public const int MaxDepth = 12;

    /// <summary>The fields of a struct type, in catalog order. Empty for every other type.</summary>
    public static IReadOnlyList<(string Name, string Type)> StructFields(string? type)
    {
        if (StructBody(type) is not { } body) return [];

        var fields = new List<(string, string)>();
        foreach (var field in SplitTopLevel(body))
        {
            // A field is "name:type". A field name cannot contain a colon, so the first one splits
            // it correctly even when the type is itself a nested struct or a map.
            var colon = field.IndexOf(':');
            if (colon < 0) continue;

            var name = field[..colon].Trim();
            var fieldType = field[(colon + 1)..].Trim();
            if (name.Length > 0) fields.Add((name, fieldType));
        }
        return fields;
    }

    /// <summary>True for a type the tree can open into child nodes.</summary>
    public static bool IsStruct(string? type) => StructBody(type) is not null;

    /// <summary>
    /// True for a repeated type. The UI already has a VECTOR tag for a reading that is more than
    /// one number, and an array column is the wire form of exactly that.
    /// </summary>
    public static bool IsArray(string? type) =>
        type?.TrimStart().StartsWith("array<", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>True for a column that carries a determination rather than a quantity.</summary>
    public static bool IsBoolean(string? type) =>
        type?.Trim().Equals("boolean", StringComparison.OrdinalIgnoreCase) ?? false;

    private static string? StructBody(string? type)
    {
        var trimmed = type?.Trim();
        if (trimmed is null || !trimmed.StartsWith(StructPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var close = trimmed.LastIndexOf('>');
        return close < StructPrefix.Length ? null : trimmed[StructPrefix.Length..close];
    }

    /// <summary>
    /// Splits on commas that are not inside a nested type, so <c>a:int,b:map&lt;string,int&gt;</c>
    /// yields two fields rather than three.
    /// </summary>
    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < body.Length; i++)
        {
            switch (body[i])
            {
                case '<' or '(':
                    depth++;
                    break;
                case '>' or ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return body[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return body[start..];
    }
}
