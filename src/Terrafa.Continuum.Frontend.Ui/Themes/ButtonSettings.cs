using Avalonia.Controls;

namespace Terrafa.Continuum.Frontend.Themes;

public static class ButtonSettings
{
    public const double MaxCornerRadius = 18;

    private static IResourceDictionary? registeredResources;

    public static double IdleEmbossStrength { get; private set; } = 0.15;

    public static double CornerRadius { get; private set; } = 12;

    public static event Action? Changed;

    public static void RegisterResources(IResourceDictionary resources)
    {
        registeredResources = resources;
        WriteResources();
    }

    public static void SetIdleEmbossStrength(double strength)
    {
        var clamped = Math.Clamp(strength, 0, 1);
        if (Math.Abs(IdleEmbossStrength - clamped) < 0.0001) return;
        IdleEmbossStrength = clamped;
        WriteResources();
        Changed?.Invoke();
    }

    public static void SetCornerRadius(double radius)
    {
        var clamped = Math.Clamp(radius, 0, MaxCornerRadius);
        if (Math.Abs(CornerRadius - clamped) < 0.0001) return;
        CornerRadius = clamped;
        WriteResources();
        Changed?.Invoke();
    }

    private static void WriteResources()
    {
        if (registeredResources is null) return;
        registeredResources["EmbossIdleStrength"] = IdleEmbossStrength;
        registeredResources["EmbossCornerRadius"] = CornerRadius;
    }
}
