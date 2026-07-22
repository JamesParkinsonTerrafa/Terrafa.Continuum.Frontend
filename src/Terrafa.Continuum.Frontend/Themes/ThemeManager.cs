namespace Terrafa.Continuum.Frontend.Themes;

public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    public static event Action? Changed;

    public static void Toggle() => SetLight(!IsLight);

    public static void SetLight(bool light)
    {
        if (IsLight == light) return;
        IsLight = light;
        Palette.Apply(light);
        Changed?.Invoke();
    }
}
