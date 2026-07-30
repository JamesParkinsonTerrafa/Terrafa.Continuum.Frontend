// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public static class NoiseOverlay
{
    private const int FieldWidth = 1560;
    private const int FieldHeight = 980;
    private const int Seed = 902611;
    private const int WarpLatticeCellSize = 512;
    private const int MinWavelength = 48;

    private static readonly List<WeakReference<Border>> liveOverlays = [];
    private static ImageBrush? cachedBrush;
    private static DispatcherTimer? rebuildTimer;

    static NoiseOverlay()
    {
        GrainSettings.IntensityChanged += ApplyIntensity;
        GrainSettings.FieldChanged += ScheduleRebuild;
        ThemeManager.Changed += () => cachedBrush = null;
    }

    public static void Attach(UserControl view)
    {
        if (view.Content is not Control existing)
        {
            return;
        }

        view.Content = null;
        var layers = new Panel();
        layers.Children.Add(existing);
        var overlay = new Border
        {
            Background = GetBrush(),
            IsHitTestVisible = false,
            Opacity = GrainSettings.Intensity / GrainSettings.MaxIntensity
        };
        layers.Children.Add(overlay);
        liveOverlays.Add(new WeakReference<Border>(overlay));
        view.Content = layers;
    }

    internal static void RebuildNow()
    {
        rebuildTimer?.Stop();
        RebuildBrush();
    }

    private static void ApplyIntensity() =>
        ForEachLiveOverlay(overlay => overlay.Opacity = GrainSettings.Intensity / GrainSettings.MaxIntensity);

    private static void ScheduleRebuild()
    {
        if (rebuildTimer is null)
        {
            rebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            rebuildTimer.Tick += (_, _) =>
            {
                rebuildTimer!.Stop();
                RebuildBrush();
            };
        }
        rebuildTimer.Stop();
        rebuildTimer.Start();
    }

    private static void RebuildBrush()
    {
        cachedBrush = CreateBrush();
        ForEachLiveOverlay(overlay => overlay.Background = cachedBrush);
    }

    private static void ForEachLiveOverlay(Action<Border> action)
    {
        for (var i = liveOverlays.Count - 1; i >= 0; i--)
        {
            if (liveOverlays[i].TryGetTarget(out var overlay))
            {
                action(overlay);
            }
            else
            {
                liveOverlays.RemoveAt(i);
            }
        }
    }

    private static ImageBrush GetBrush() => cachedBrush ??= CreateBrush();

    private static ImageBrush CreateBrush() => new(BuildTexture())
    {
        Stretch = Stretch.Fill
    };

    private static WriteableBitmap BuildTexture()
    {
        var field = BuildNoiseField();
        var useDarkSheen = ThemeManager.IsLight;
        var pixels = new byte[FieldWidth * FieldHeight * 4];
        for (var i = 0; i < field.Length; i++)
        {
            var level = Math.Clamp((field[i] + 1) / 2, 0, 1);
            var alpha = (byte)Math.Round(level * GrainSettings.MaxIntensity);
            var tone = useDarkSheen ? (byte)0 : alpha;
            var offset = i * 4;
            pixels[offset] = tone;
            pixels[offset + 1] = tone;
            pixels[offset + 2] = tone;
            pixels[offset + 3] = alpha;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(FieldWidth, FieldHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < FieldHeight; row++)
        {
            Marshal.Copy(pixels, row * FieldWidth * 4, buffer.Address + row * buffer.RowBytes, FieldWidth * 4);
        }
        return bitmap;
    }

    private static double[] BuildNoiseField()
    {
        var rng = new Random(Seed);
        var waves = CreateWaves(rng);
        var warpLatticeX = CreateLattice(rng, WarpLatticeCellSize);
        var warpLatticeY = CreateLattice(rng, WarpLatticeCellSize);
        var warpStrength = GrainSettings.WarpStrength;
        var fineGrain = GrainSettings.FineGrain;

        var field = new double[FieldWidth * FieldHeight];
        Parallel.For(0, FieldHeight, y =>
        {
            var grainRng = new Random(Seed * 31 + y);
            for (var x = 0; x < FieldWidth; x++)
            {
                var sampleX = x + SampleLattice(warpLatticeX, x, y) * warpStrength;
                var sampleY = y + SampleLattice(warpLatticeY, x, y) * warpStrength;
                var total = 0.0;
                foreach (var wave in waves)
                {
                    total += Math.Sin(wave.FreqX * sampleX + wave.FreqY * sampleY + wave.Phase) * wave.Amplitude;
                }
                total += (grainRng.NextDouble() * 2 - 1) * fineGrain;
                field[y * FieldWidth + x] = total;
            }
        });

        var peak = field.Max(Math.Abs);
        if (peak > 0)
        {
            for (var i = 0; i < field.Length; i++)
            {
                field[i] /= peak;
            }
        }
        return field;
    }

    private sealed record WaveComponent(double FreqX, double FreqY, double Phase, double Amplitude);

    private static List<WaveComponent> CreateWaves(Random rng)
    {
        var wavelengths = new List<int>();
        for (var wavelength = GrainSettings.BaseWavelength; wavelength >= MinWavelength; wavelength /= 2)
        {
            wavelengths.Add(wavelength);
        }

        var baseAngle = rng.NextDouble() * Math.PI;
        var waves = new List<WaveComponent>();
        for (var i = 0; i < wavelengths.Count; i++)
        {
            var wavelength = wavelengths[i];
            var frequency = Math.PI * 2 / wavelength;
            var amplitude = Math.Pow(wavelength, GrainSettings.SpectralSlope);
            var jitter = (rng.NextDouble() - 0.5) * Math.PI / 16;
            var angle = baseAngle + (i - (wavelengths.Count - 1) / 2.0) * Math.PI / 9 + jitter;
            waves.Add(new WaveComponent(
                frequency * Math.Cos(angle),
                frequency * Math.Sin(angle),
                rng.NextDouble() * Math.PI * 2,
                amplitude));
        }
        return waves;
    }

    private sealed record GradientLattice(double[] GradX, double[] GradY, int CountX, int CountY, int CellSize);

    private static GradientLattice CreateLattice(Random rng, int cellSize)
    {
        var countX = FieldWidth / cellSize + 2;
        var countY = FieldHeight / cellSize + 2;
        var gradX = new double[countX * countY];
        var gradY = new double[countX * countY];
        for (var i = 0; i < gradX.Length; i++)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            gradX[i] = Math.Cos(angle);
            gradY[i] = Math.Sin(angle);
        }
        return new GradientLattice(gradX, gradY, countX, countY, cellSize);
    }

    private static double SampleLattice(GradientLattice lattice, double x, double y)
    {
        var gridX = x / lattice.CellSize;
        var gridY = y / lattice.CellSize;
        var cellX = Math.Clamp((int)Math.Floor(gridX), 0, lattice.CountX - 2);
        var cellY = Math.Clamp((int)Math.Floor(gridY), 0, lattice.CountY - 2);
        var fracX = gridX - cellX;
        var fracY = gridY - cellY;

        var rowTop = cellY * lattice.CountX;
        var rowBottom = rowTop + lattice.CountX;
        var d00 = CornerDot(lattice, rowTop + cellX, fracX, fracY);
        var d10 = CornerDot(lattice, rowTop + cellX + 1, fracX - 1, fracY);
        var d01 = CornerDot(lattice, rowBottom + cellX, fracX, fracY - 1);
        var d11 = CornerDot(lattice, rowBottom + cellX + 1, fracX - 1, fracY - 1);

        var blendX = Fade(fracX);
        var blendY = Fade(fracY);
        return Lerp(Lerp(d00, d10, blendX), Lerp(d01, d11, blendX), blendY);
    }

    private static double CornerDot(GradientLattice lattice, int index, double offsetX, double offsetY) =>
        lattice.GradX[index] * offsetX + lattice.GradY[index] * offsetY;

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
