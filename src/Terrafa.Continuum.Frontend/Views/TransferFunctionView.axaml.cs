using Avalonia;
using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class TransferFunctionView : UserControl
{
    private const double DomainStart = 0.34;
    private const double DomainEnd = 3.2;
    private const double InputSigma = 0.15;

    public TransferFunctionView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public TransferFunctionView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        ConfigureStageArrows();
        ConfigureStageCharts();
        ConfigureResultChart();

        NoiseOverlay.Attach(this);
    }

    private void ConfigureStageArrows()
    {
        foreach (var arrow in new[] { Arrow1, Arrow2, Arrow3 })
        {
            arrow.Edges =
            [
                new Edge
                {
                    From = new Point(182, 0),
                    To = new Point(182, 32),
                    Stroke = Palette.TextGhost,
                    Thickness = 2,
                    ArrowAtEnd = true
                }
            ];
        }
    }

    private void ConfigureStageCharts()
    {
        StageGChart.MarginLeft = 8;
        StageGChart.MarginRight = 6;
        StageGChart.MarginTop = 8;
        StageGChart.MarginBottom = 8;
        StageGChart.XMin = 0;
        StageGChart.XMax = 1.2;
        StageGChart.YMin = 0;
        StageGChart.YMax = 1.44;
        StageGChart.Series =
        [
            new ChartSeries
            {
                Points = ChartSampling.Sample(x => x * x, 0, 1.2),
                Stroke = Palette.Cyan,
                Thickness = 1.6
            }
        ];
        StageGChart.Labels = [new ChartLabel(1.15, 1.28, "v = x²", Palette.TextFaint, true, 8)];

        StageFChart.MarginLeft = 8;
        StageFChart.MarginRight = 6;
        StageFChart.MarginTop = 8;
        StageFChart.MarginBottom = 8;
        StageFChart.XMin = 0.1;
        StageFChart.XMax = 3.2;
        StageFChart.YMin = 0;
        StageFChart.YMax = 8;
        StageFChart.Series =
        [
            new ChartSeries
            {
                Points = ChartSampling.Sample(u => 1 / u, 0.12, 3.2),
                Stroke = Palette.Amber,
                Thickness = 1.6
            }
        ];
        StageFChart.Labels = [new ChartLabel(3.05, 7.1, "w = 1/u", Palette.TextFaint, true, 8)];
    }

    private void ConfigureResultChart()
    {
        static double Transfer(double x) => 1 / (x * x);
        static double SigmaOut(double x) => Math.Abs(-2 / (x * x * x)) * InputSigma;

        ResultChart.MarginLeft = 60;
        ResultChart.MarginRight = 20;
        ResultChart.MarginTop = 20;
        ResultChart.MarginBottom = 36;
        ResultChart.XMin = DomainStart;
        ResultChart.XMax = DomainEnd;
        ResultChart.YMin = 0;
        ResultChart.YMax = 9;
        ResultChart.VerticalGridValues = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0];
        ResultChart.HorizontalGridValues = [3.0, 6.0];
        ResultChart.XTicks =
        [
            new AxisTick(0.5, "0.5"),
            new AxisTick(1.0, "1.0"),
            new AxisTick(1.5, "1.5"),
            new AxisTick(2.0, "2.0"),
            new AxisTick(2.5, "2.5"),
            new AxisTick(3.0, "3.0")
        ];
        ResultChart.Regions = [new ChartRegion(DomainStart, 0.45, Palette.RedZoneFill)];
        ResultChart.Band = new ChartBand(
            ChartSampling.Sample(x => Transfer(x) + SigmaOut(x), DomainStart, DomainEnd, 200),
            ChartSampling.Sample(x => Math.Max(Transfer(x) - SigmaOut(x), 0), DomainStart, DomainEnd, 200),
            Palette.AmberFill);
        ResultChart.Series =
        [
            new ChartSeries
            {
                Points = ChartSampling.Sample(Transfer, DomainStart, DomainEnd, 200),
                Stroke = Palette.Amber,
                Thickness = 2
            }
        ];
        ResultChart.Markers = [new ChartMarker(1.0, Transfer(1.0), 4, Palette.Green)];
        ResultChart.Labels =
        [
            new ChartLabel(1.05, 1.35, "x=1.0 → h=1.00 ±0.30", Palette.Green, false),
            new ChartLabel(3.15, 8.35, "h(x) ± σₕ(x)", Palette.Amber, true, 11),
            new ChartLabel(0.36, 8.55, "x→0: pole,", Palette.Red, false, 9),
            new ChartLabel(0.36, 8.15, "σ blows up", Palette.Red, false, 9)
        ];
        ResultChart.XAxisTitle = "x (normalised tank_01.level)";
        ResultChart.YAxisTitle = "h(x) = 1/x²";
    }
}
