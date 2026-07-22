using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public DashboardView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();
        PositionsFootnote.Text = $"as-of t = {snapshot.AsOf:HH:mm:ss} · derived from 𝓕ₜ, no future leakage";

        BuildPositionRows(snapshot);
        BuildLeaderboardRows(snapshot);
        ConfigureCalibrationChart(snapshot);
        ConfigureWealthChart(snapshot);
        ConfigureSurvivalChart(snapshot);
        ConfigureIntensityChart(snapshot);
    }

    private static ColumnDefinitions PositionColumns() => new("1.5*,1.2*,0.7*,0.8*");

    private void BuildPositionRows(DataSnapshot snapshot)
    {
        var header = new Grid { ColumnDefinitions = PositionColumns(), Margin = new Thickness(0, 2) };
        AddCell(header, 0, HeaderCell("COMMODITY"));
        AddCell(header, 1, HeaderCell("QTY (±σ)"));
        AddCell(header, 2, HeaderCell("Δ 24H"));
        AddCell(header, 3, HeaderCell("TREND"));
        PositionRows.Children.Add(header);

        foreach (var position in snapshot.Positions)
        {
            var row = new Grid { ColumnDefinitions = PositionColumns() };
            var container = new Border
            {
                BorderBrush = Palette.GridFaint,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 5),
                Child = row
            };

            AddCell(row, 0, BodyCell(position.Commodity, Palette.TextBright, 11));

            var quantity = new TextBlock { FontSize = 11, Foreground = Palette.TextStrong };
            quantity.Inlines =
            [
                new Avalonia.Controls.Documents.Run(position.Quantity + " "),
                new Avalonia.Controls.Documents.Run(position.Sigma) { Foreground = Palette.Cyan }
            ];
            AddCell(row, 1, quantity);

            var deltaBrush = position.DeltaUp ? Palette.Green : Palette.Red;
            AddCell(row, 2, BodyCell(position.Delta, deltaBrush, 11));

            var sparkline = new Sparkline
            {
                Values = position.Trend,
                Stroke = deltaBrush,
                Width = 70,
                Height = 18,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            AddCell(row, 3, sparkline);

            PositionRows.Children.Add(container);
        }
    }

    private static ColumnDefinitions LeaderboardColumns() => new("0.5*,2*,1*,0.8*");

    private void BuildLeaderboardRows(DataSnapshot snapshot)
    {
        var header = new Grid { ColumnDefinitions = LeaderboardColumns(), Margin = new Thickness(0, 2) };
        AddCell(header, 0, HeaderCell("RK"));
        AddCell(header, 1, HeaderCell("MODEL"));
        AddCell(header, 2, HeaderCell("Σ LOG S"));
        AddCell(header, 3, HeaderCell("24H"));
        LeaderboardRows.Children.Add(header);

        foreach (var entry in snapshot.Leaderboard)
        {
            var row = new Grid { ColumnDefinitions = LeaderboardColumns() };
            var container = new Border
            {
                BorderBrush = Palette.GridFaint,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 5),
                Child = row
            };

            var rankBrush = entry.Rank == "1" ? Palette.Amber : Palette.TextMuted;
            var modelBrush = entry.Dimmed ? Palette.TextMuted : Palette.TextBright;
            var scoreBrush = entry.Dimmed ? Palette.TextMuted : Palette.TextStrong;
            var deltaBrush = entry.Direction switch
            {
                1 => Palette.Green,
                -1 => Palette.Red,
                _ => Palette.TextMuted
            };

            AddCell(row, 0, BodyCell(entry.Rank, rankBrush, 11));
            AddCell(row, 1, BodyCell(entry.Model, modelBrush, 11));
            AddCell(row, 2, BodyCell(entry.Score, scoreBrush, 11));
            AddCell(row, 3, BodyCell(entry.Delta, deltaBrush, 11));

            LeaderboardRows.Children.Add(container);
        }
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

    private static TextBlock BodyCell(string text, IBrush brush, double fontSize) => new()
    {
        Text = text,
        FontSize = fontSize,
        Foreground = brush
    };

    private void ConfigureCalibrationChart(DataSnapshot snapshot)
    {
        CalibrationChart.MarginLeft = 34;
        CalibrationChart.MarginRight = 14;
        CalibrationChart.MarginTop = 12;
        CalibrationChart.MarginBottom = 30;
        CalibrationChart.XMin = 0;
        CalibrationChart.XMax = 1;
        CalibrationChart.YMin = 0;
        CalibrationChart.YMax = 1;
        CalibrationChart.Series =
        [
            new ChartSeries
            {
                Points = [new Point(0, 0), new Point(1, 1)],
                Stroke = Palette.TextFaint,
                Thickness = 1,
                Dashes = [4, 4]
            }
        ];
        CalibrationChart.Markers = snapshot.Calibration
            .Select(point => new ChartMarker(
                point.Predicted,
                point.Observed,
                4,
                point.OverConfident ? Palette.Amber : Palette.Cyan))
            .ToList();
        CalibrationChart.Labels =
        [
            new ChartLabel(0.66, 0.40, "over-confident 0.6–0.7", Palette.Amber, false, 9)
        ];
        CalibrationChart.XAxisTitle = "PREDICTED p";
        CalibrationChart.YAxisTitle = "OBSERVED f";
    }

    private void ConfigureWealthChart(DataSnapshot snapshot)
    {
        WealthChart.MarginLeft = 36;
        WealthChart.MarginRight = 18;
        WealthChart.MarginTop = 14;
        WealthChart.MarginBottom = 28;
        WealthChart.XMin = 0;
        WealthChart.XMax = 9;
        WealthChart.YMin = 0;
        WealthChart.YMax = 22;
        WealthChart.ThresholdY = snapshot.WealthThreshold;
        WealthChart.ThresholdLabel = "1/α = 20";
        WealthChart.Series = snapshot.WealthSeries.Select((series, index) => new ChartSeries
        {
            Points = series.Xs.Zip(series.Ys, (x, y) => new Point(x, y)).ToList(),
            Stroke = index == 0 ? Palette.Green : Palette.Red,
            Thickness = 1.5,
            Label = series.Label,
            LabelAt = index == 0 ? new Point(9, 3.2) : new Point(8.9, 12.5),
            LabelAnchorEnd = true
        }).ToList();
        var falsified = snapshot.WealthSeries[1];
        WealthChart.Markers =
        [
            new ChartMarker(falsified.Xs[^1], falsified.Ys[^1], 4, Palette.Red)
        ];
        WealthChart.XAxisTitle = "EVENT STREAM t →";
    }

    private void ConfigureSurvivalChart(DataSnapshot snapshot)
    {
        SurvivalChart.MarginLeft = 36;
        SurvivalChart.MarginRight = 18;
        SurvivalChart.MarginTop = 14;
        SurvivalChart.MarginBottom = 28;
        SurvivalChart.XMin = 0;
        SurvivalChart.XMax = 60;
        SurvivalChart.YMin = 0;
        SurvivalChart.YMax = 1;
        var strokes = new IBrush[] { Palette.Cyan, Palette.TextMuted, Palette.Red };
        var labelPoints = new Point?[] { new Point(59, 0.44), new Point(59, 0.17), new Point(59, 0.065) };
        SurvivalChart.Series = snapshot.SurvivalSeries.Select((series, index) => new ChartSeries
        {
            Points = series.Xs.Zip(series.Ys, (x, y) => new Point(x, y)).ToList(),
            Stroke = strokes[index],
            Thickness = 1.5,
            Dashes = index == 1 ? [5, 4] : null,
            Label = series.Label,
            LabelAt = labelPoints[index],
            LabelAnchorEnd = true
        }).ToList();
        SurvivalChart.XAxisTitle = "t (days) →";
        SurvivalChart.YAxisTitle = "S(t)";
    }

    private void ConfigureIntensityChart(DataSnapshot snapshot)
    {
        IntensityChart.MarginLeft = 20;
        IntensityChart.MarginRight = 18;
        IntensityChart.MarginTop = 14;
        IntensityChart.MarginBottom = 28;
        IntensityChart.XMin = 0;
        IntensityChart.XMax = 10;
        IntensityChart.YMin = 0;
        IntensityChart.YMax = 1.7;
        IntensityChart.BarValues = snapshot.IntensityBars;
        IntensityChart.BarBrushes = snapshot.IntensityBars
            .Select((_, index) => (IBrush)(index switch
            {
                < 6 => Palette.BarFillLow,
                < 8 => Palette.BarFillMid,
                _ => Palette.BarFillHigh
            }))
            .ToList();
        IntensityChart.Series =
        [
            new ChartSeries
            {
                Points = snapshot.IntensityLine
                    .Select((value, index) => new Point(index + 0.5, value))
                    .ToList(),
                Stroke = Palette.Amber,
                Thickness = 1.5,
                Label = "λ(t) | temp ↑",
                LabelAt = new Point(9.9, 1.55),
                LabelAnchorEnd = true
            }
        ];
        IntensityChart.XAxisTitle = "WEEKS · marks = (vol, tank, grade)";
    }
}
