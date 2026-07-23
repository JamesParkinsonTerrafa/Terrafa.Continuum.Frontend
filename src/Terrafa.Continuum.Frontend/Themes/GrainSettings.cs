namespace Terrafa.Continuum.Frontend.Themes;

public static class GrainSettings
{
    public const double MaxIntensity = 30;

    public static readonly int[] BaseWavelengthOptions = [128, 256, 512];

    public static double Intensity { get; private set; } = 2;
    public static int BaseWavelength { get; private set; } = 512;
    public static double SpectralSlope { get; private set; } = 0.0;
    public static double WarpStrength { get; private set; } = 51;
    public static double FineGrain { get; private set; } = 3.0;

    public static event Action? IntensityChanged;
    public static event Action? FieldChanged;

    public static void SetIntensity(double value)
    {
        Intensity = Math.Clamp(value, 0, MaxIntensity);
        IntensityChanged?.Invoke();
    }

    public static void SetBaseWavelength(int value)
    {
        BaseWavelength = value;
        FieldChanged?.Invoke();
    }

    public static void SetSpectralSlope(double value)
    {
        SpectralSlope = Math.Clamp(value, 0, 2);
        FieldChanged?.Invoke();
    }

    public static void SetWarpStrength(double value)
    {
        WarpStrength = Math.Clamp(value, 0, 100);
        FieldChanged?.Invoke();
    }

    public static void SetFineGrain(double value)
    {
        FineGrain = Math.Clamp(value, 0, 10);
        FieldChanged?.Invoke();
    }
}
