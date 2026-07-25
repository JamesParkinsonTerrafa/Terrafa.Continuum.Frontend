using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Terrafa.Continuum.Frontend.Themes;

public static class ThemeManager
{
    public static bool IsLight { get; private set; } = true;

    public static event Action? Changed;

    public static void Initialize(IResourceDictionary resources)
    {
        Palette.RegisterResources(resources);
        Palette.Apply(IsLight);
        ApplyVariant();
    }

    public static void Toggle() => SetLight(!IsLight);

    public static void SetLight(bool light)
    {
        if (IsLight == light) return;
        IsLight = light;
        Palette.Apply(light);
        ApplyVariant();
        Changed?.Invoke();
    }

    private static void ApplyVariant()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
