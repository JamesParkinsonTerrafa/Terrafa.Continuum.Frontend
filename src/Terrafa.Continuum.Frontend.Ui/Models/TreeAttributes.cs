// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

[AttributeUsage(AttributeTargets.Property)]
public sealed class TreeNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class TreeTagAttribute(string tag) : Attribute
{
    public string Tag { get; } = tag;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class TreeNewAttribute : Attribute;
