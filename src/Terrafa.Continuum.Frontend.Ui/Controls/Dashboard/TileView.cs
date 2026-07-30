// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Dashboard;

/// <summary>
/// Renders one dashboard tile. Every chart form here shows its bounds natively — whiskers on bars,
/// upper and lower traces on lines, a ± column on tables — because a figure drawn without them
/// reads as exact, and nothing in this app is.
/// </summary>
public static class TileView
{
    private static readonly IBrush[] SeriesBrushes =
        [Palette.Cyan, Palette.Amber, Palette.Green, Palette.Purple, Palette.Red];

    public static Control Build(DashboardTile tile)
    {
        var body = BuildBody(tile, out var footnote);

        var layout = new DockPanel { Margin = new Thickness(10, 8) };
        DockPanel.SetDock(footnote, Avalonia.Controls.Dock.Bottom);
        layout.Children.Add(BuildHeader(tile));
        layout.Children.Add(footnote);
        layout.Children.Add(body);
        return layout;
    }

    private static Control BuildHeader(DashboardTile tile)
    {
        var name = new TextBlock
        {
            Text = tile.Name,
            FontSize = 11,
            Foreground = Palette.TextBright,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var kind = new TextBlock
        {
            Text = DashboardTile.KindLabel(tile.Kind),
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(row, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(kind, Avalonia.Controls.Dock.Right);
        row.Children.Add(kind);
        row.Children.Add(name);

        var header = new Border
        {
            BorderBrush = Palette.GridFaint,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Child = row
        };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        return header;
    }

    private static Control BuildBody(DashboardTile tile, out TextBlock footnote)
    {
        if (!tile.IsWired)
        {
            footnote = Footnote("double-click to name the tile and pick its data sources", Palette.TextFaint);
            return Placeholder("EMPTY TILE", "no data source wired", Palette.TextGhost);
        }

        var resolved = tile.Sources
            .Select(source => (Source: source, Series: TileData.Resolve(source)))
            .ToList();

        if (resolved.All(entry => entry.Series is null))
        {
            footnote = Footnote("source no longer mounted — rewire the tile", Palette.Red);
            return Placeholder("SOURCE MISSING", string.Join(" · ", tile.Sources.Select(s => s.Display)), Palette.Red);
        }

        var series = resolved.Where(entry => entry.Series is not null)
            .Select(entry => entry.Series!)
            .ToList();

        // Mounted and wired, but with no number behind it. Distinct from the case above: nothing is
        // wrong with the wiring, so the tile says what it is holding rather than telling the
        // operator to rewire it — and it separates a column that has not been sampled yet from one
        // that reads as text and never will plot.
        var silent = series.Where(entry => !entry.HasValue).ToList();
        if (silent.Count > 0)
        {
            var categorical = silent.Where(entry => Reads(entry)).ToList();
            footnote = Footnote(
                categorical.Count > 0
                    ? "the source reads, but not as a number a chart can take"
                    : "the source is mounted — it carries no value behind it yet",
                Palette.TextFaint);
            return Placeholder(
                "NO READING",
                string.Join(" · ", silent.Select(entry => Reads(entry) ? $"{entry.Label} = {entry.Display}" : entry.Label)),
                Palette.Amber);
        }

        // The rule the master switch exists for: with variance on, a tile that cannot draw bounds
        // blanks rather than showing a bare central estimate that would read as certain.
        if (VarianceSettings.Enabled && series.Any(entry => !entry.HasVariance))
        {
            var bare = series.Where(entry => !entry.HasVariance).Select(entry => entry.Label);
            footnote = Footnote("switch variance off to prototype without σ", Palette.TextFaint);
            return Placeholder(
                "NO σ — TILE BLANK",
                $"not wired for variance: {string.Join(" · ", bare)}",
                Palette.Amber);
        }

        var showBounds = VarianceSettings.Enabled;
        footnote = Footnote(BuildFootnote(series, showBounds), showBounds ? Palette.TextFaint : Palette.Amber);

        return tile.Kind switch
        {
            TileKind.Line => BuildLineChart(series, showBounds),
            TileKind.Bar => BuildBarChart(series, showBounds),
            _ => BuildTable(series, showBounds)
        };
    }

    /// <summary>Whether the source shows something — as opposed to the "—" of a leaf never read.</summary>
    private static bool Reads(TileSeries series) =>
        series.Display.Length > 0 && series.Display != "—";

    private static string BuildFootnote(IReadOnlyList<TileSeries> series, bool showBounds)
    {
        var provisional = series.Any(entry => entry.IsProvisional) ? " · ⚠ provisional source" : "";
        if (!showBounds) return $"{series.Count} source(s) · VARIANCE OFF — central estimate only{provisional}";

        var asserted = series.Count(entry => entry.IsAssertedSigma);
        var origin = asserted == 0
            ? series.Any(entry => entry.SigmaHistory.Count > 0) ? " · σ(x) from tree" : ""
            : $" · {asserted} σ asserted from a figure";
        return $"{series.Count} source(s) · bounds at ±1σ{origin}{provisional}";
    }

    private static Control BuildLineChart(IReadOnlyList<TileSeries> series, bool showBounds)
    {
        var length = series.Max(entry => entry.History.Count);
        if (length < 2) return Placeholder("NO SERIES", "source carries no history to plot", Palette.TextGhost);

        var chart = new LineChart
        {
            MarginLeft = 46,
            MarginRight = 14,
            MarginTop = 10,
            MarginBottom = 22,
            XMin = 0,
            XMax = length - 1
        };

        var lines = new List<ChartSeries>();
        var low = double.MaxValue;
        var high = double.MinValue;

        for (var i = 0; i < series.Count; i++)
        {
            var entry = series[i];
            if (entry.History.Count < 2) continue;

            // A nominated σ is drawn in the provisional colour so an asserted band never passes for
            // one the tree carried.
            var stroke = entry.IsAssertedSigma && showBounds
                ? Palette.Purple
                : SeriesBrushes[i % SeriesBrushes.Length];
            var points = entry.History.Select((value, index) => new Point(index, value)).ToList();

            List<Point>? upper = null;
            List<Point>? lower = null;
            if (showBounds && entry.HasVariance)
            {
                // SigmaAt falls back to the flat figure, so a heteroscedastic σ(x) breathes and a
                // flat one stays parallel without either needing a separate path.
                upper = points.Select((point, index) => new Point(point.X, point.Y + entry.SigmaAt(index))).ToList();
                lower = points.Select((point, index) => new Point(point.X, point.Y - entry.SigmaAt(index))).ToList();
                low = Math.Min(low, lower.Min(point => point.Y));
                high = Math.Max(high, upper.Max(point => point.Y));
            }

            low = Math.Min(low, points.Min(point => point.Y));
            high = Math.Max(high, points.Max(point => point.Y));

            lines.Add(new ChartSeries
            {
                Points = points,
                Stroke = stroke,
                Thickness = 1.5,
                Upper = upper,
                Lower = lower,
                BoundFill = upper is null ? null : Fill(stroke)
            });
        }

        var (yMin, yMax) = Padded(low, high);
        chart.YMin = yMin;
        chart.YMax = yMax;

        var grid = FunctionTrace.NiceSteps(yMin, yMax, 4);
        chart.HorizontalGridValues = grid;
        chart.Labels = grid
            .Select(value => new ChartLabel(-0.4, value, FormatValue(value), Palette.TextFaint, true, 9))
            .ToArray();
        chart.Series = lines;
        return chart;
    }

    private static Control BuildBarChart(IReadOnlyList<TileSeries> series, bool showBounds)
    {
        var values = series.Select(entry => entry.Value).ToList();
        var sigmas = series
            .Select(entry => showBounds && entry.HasVariance ? entry.Sigma : double.NaN)
            .ToList();
        var strokes = series
            .Select((entry, index) => entry.IsAssertedSigma && showBounds
                ? Palette.Purple
                : SeriesBrushes[index % SeriesBrushes.Length])
            .ToList();

        var low = Math.Min(0, values.Min());
        var high = values.Max();
        for (var i = 0; i < values.Count; i++)
        {
            if (double.IsNaN(sigmas[i])) continue;
            low = Math.Min(low, values[i] - sigmas[i]);
            high = Math.Max(high, values[i] + sigmas[i]);
        }

        var (yMin, yMax) = Padded(low, high);
        var chart = new LineChart
        {
            MarginLeft = 46,
            MarginRight = 14,
            MarginTop = 10,
            MarginBottom = 22,
            XMin = 0,
            XMax = Math.Max(values.Count, 1),
            YMin = yMin,
            YMax = yMax,
            BarValues = values,
            BarSigmas = sigmas,
            BarBrushes = strokes.Select(stroke => (IBrush)Fill(stroke, 0.55)).ToList(),
            BarWhiskerBrush = Palette.TextSub,
            XTicks = series
                .Select((entry, index) => new AxisTick(index + 0.5, Trim(entry.Label, 14)))
                .ToArray()
        };

        var grid = FunctionTrace.NiceSteps(yMin, yMax, 4);
        chart.HorizontalGridValues = grid;
        chart.Labels = grid
            .Select(value => new ChartLabel(-0.1, value, FormatValue(value), Palette.TextFaint, true, 9))
            .ToArray();
        return chart;
    }

    private static Control BuildTable(IReadOnlyList<TileSeries> series, bool showBounds)
    {
        var columns = showBounds ? "1.5*,0.9*,0.8*,0.9*" : "1.9*,1.1*,0.8*";
        var rows = new StackPanel();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(columns), Margin = new Thickness(0, 0, 0, 2) };
        AddCell(header, 0, HeaderCell("SOURCE"));
        AddCell(header, 1, HeaderCell("VALUE"));
        if (showBounds)
        {
            AddCell(header, 2, HeaderCell("±1σ"));
            AddCell(header, 3, HeaderCell("σ SOURCE"));
        }
        else
        {
            AddCell(header, 2, HeaderCell("UNIT"));
        }
        rows.Children.Add(header);

        foreach (var entry in series)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(columns) };
            AddCell(row, 0, BodyCell(Trim(entry.Label, 26), entry.IsProvisional ? Palette.Purple : Palette.TextBright));
            AddCell(row, 1, BodyCell(FormatValue(entry.Value), Palette.TextStrong));
            if (showBounds)
            {
                // Quoted the way the card that produced it quotes it — a σ propagated up a chain
                // lands on 152.11813, and printing that here while the network says "± 152" reads
                // as the two screens disagreeing.
                var sigmaBrush = entry.IsAssertedSigma ? Palette.Purple : Palette.Cyan;
                AddCell(row, 2, BodyCell($"± {MeasureNumerics.FormatSigma(entry.Sigma)}", sigmaBrush));
                AddCell(row, 3, BodyCell(Trim(entry.SigmaNote, 20), sigmaBrush));
            }
            else
            {
                AddCell(row, 2, BodyCell(entry.Unit, Palette.TextMuted));
            }

            rows.Children.Add(new Border
            {
                BorderBrush = Palette.GridFaint,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 5),
                Child = row
            });
        }

        return new ScrollViewer { Content = rows };
    }

    private static void AddCell(Grid grid, int column, Control cell)
    {
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static TextBlock HeaderCell(string text) => new()
    {
        Text = text,
        FontSize = 9,
        LetterSpacing = 1,
        Foreground = Palette.TextFaint
    };

    private static TextBlock BodyCell(string text, IBrush brush) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static Control Placeholder(string title, string detail, IBrush brush)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 10,
            LetterSpacing = 1.5,
            Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 10,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return stack;
    }

    private static TextBlock Footnote(string text, IBrush brush) => new()
    {
        Text = text,
        FontSize = 9,
        Margin = new Thickness(0, 6, 0, 0),
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static (double Min, double Max) Padded(double low, double high)
    {
        if (double.IsInfinity(low) || double.IsInfinity(high) || low > high) return (0, 1);
        if (Math.Abs(high - low) < 1e-9)
        {
            var nudge = Math.Max(Math.Abs(high) * 0.1, 0.5);
            return (low - nudge, high + nudge);
        }
        var padding = (high - low) * 0.12;
        return (low - padding, high + padding);
    }

    private static SolidColorBrush Fill(IBrush source, double opacity = 0.14)
    {
        var colour = source is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return new SolidColorBrush(colour, opacity);
    }

    private static string FormatValue(double value)
    {
        if (double.IsNaN(value)) return "—";
        var magnitude = Math.Abs(value);
        if (magnitude >= 10000) return value.ToString("#,##0", CultureInfo.InvariantCulture);
        if (magnitude >= 100) return value.ToString("0.#", CultureInfo.InvariantCulture);
        if (magnitude >= 1) return value.ToString("0.##", CultureInfo.InvariantCulture);
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
