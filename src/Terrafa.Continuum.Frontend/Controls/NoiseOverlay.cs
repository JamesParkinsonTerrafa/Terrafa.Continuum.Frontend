using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Terrafa.Continuum.Frontend.Controls;

public static class NoiseOverlay
{
    private const int TextureSize = 512;
    private const double MaxAlpha = 7;
    private const int Seed = 902611;

    private static ImageBrush? cachedBrush;

    public static void Attach(UserControl view)
    {
        if (view.Content is not Control existing)
        {
            return;
        }

        view.Content = null;
        var layers = new Panel();
        layers.Children.Add(existing);
        layers.Children.Add(new Border
        {
            Background = GetBrush(),
            IsHitTestVisible = false
        });
        view.Content = layers;
    }

    private static ImageBrush GetBrush()
    {
        cachedBrush ??= new ImageBrush(BuildTexture())
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, TextureSize, TextureSize, RelativeUnit.Absolute)
        };
        return cachedBrush;
    }

    private static WriteableBitmap BuildTexture()
    {
        var field = BuildNoiseField();
        var pixels = new byte[TextureSize * TextureSize * 4];
        for (var i = 0; i < field.Length; i++)
        {
            var value = field[i];
            var alpha = (byte)Math.Round(Math.Abs(value) * MaxAlpha);
            var tone = value > 0 ? alpha : (byte)0;
            var offset = i * 4;
            pixels[offset] = tone;
            pixels[offset + 1] = tone;
            pixels[offset + 2] = tone;
            pixels[offset + 3] = alpha;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(TextureSize, TextureSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var row = 0; row < TextureSize; row++)
        {
            Marshal.Copy(pixels, row * TextureSize * 4, buffer.Address + row * buffer.RowBytes, TextureSize * 4);
        }
        return bitmap;
    }

    private static double[] BuildNoiseField()
    {
        var rng = new Random(Seed);
        var field = new double[TextureSize * TextureSize];
        var octaves = new (int CellSize, double Amplitude)[]
        {
            (128, 0.30), (64, 0.35), (32, 0.40), (16, 0.45), (8, 0.55)
        };
        foreach (var (cellSize, amplitude) in octaves)
        {
            AddSmoothOctave(rng, field, cellSize, amplitude);
        }

        for (var i = 0; i < field.Length; i++)
        {
            field[i] += rng.NextDouble() * 2 - 1;
        }

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

    private static void AddSmoothOctave(Random rng, double[] field, int cellSize, double amplitude)
    {
        var gridCount = TextureSize / cellSize;
        var lattice = new double[gridCount * gridCount];
        for (var i = 0; i < lattice.Length; i++)
        {
            lattice[i] = rng.NextDouble() * 2 - 1;
        }

        for (var y = 0; y < TextureSize; y++)
        {
            var gridY = (double)y / cellSize;
            var cellY = (int)gridY;
            var blendY = Smooth(gridY - cellY);
            var nextY = (cellY + 1) % gridCount;
            cellY %= gridCount;
            for (var x = 0; x < TextureSize; x++)
            {
                var gridX = (double)x / cellSize;
                var cellX = (int)gridX;
                var blendX = Smooth(gridX - cellX);
                var nextX = (cellX + 1) % gridCount;
                cellX %= gridCount;
                var top = Lerp(lattice[cellY * gridCount + cellX], lattice[cellY * gridCount + nextX], blendX);
                var bottom = Lerp(lattice[nextY * gridCount + cellX], lattice[nextY * gridCount + nextX], blendX);
                field[y * TextureSize + x] += Lerp(top, bottom, blendY) * amplitude;
            }
        }
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
