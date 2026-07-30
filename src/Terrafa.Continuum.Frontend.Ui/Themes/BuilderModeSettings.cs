// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia.Controls;

namespace Terrafa.Continuum.Frontend.Themes;

public static class BuilderModeSettings
{
    private static IResourceDictionary? registeredResources;

    public static bool Enabled { get; private set; }

    public static event Action? Changed;

    public static void RegisterResources(IResourceDictionary resources)
    {
        registeredResources = resources;
        WriteResource();
    }

    public static void Toggle() => SetEnabled(!Enabled);

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        WriteResource();
        Changed?.Invoke();
    }

    private static void WriteResource()
    {
        if (registeredResources is null) return;
        registeredResources["BuilderPanelsVisible"] = Enabled;
        registeredResources["BuilderHintVisible"] = !Enabled;
    }
}
