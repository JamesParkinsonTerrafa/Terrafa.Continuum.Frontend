namespace Terrafa.Continuum.Frontend.Models;

public sealed class SiteAlpha
{
    public TankFarm TankFarm { get; init; } = new();

    [TreeTag("PIPELINE")]
    public BerthDelivery BerthDelivery { get; init; } = new();

    public Intake Intake { get; init; } = new();
}

public sealed class TankFarm
{
    public Tank01 Tank01 { get; init; } = new();
    public Tank02 Tank02 { get; init; } = new();

    [TreeNew]
    public Tank03 Tank03 { get; init; } = new();
}

public sealed class Tank01
{
    public Measure Level { get; init; } = new()
    {
        Display = "14,203 bbl",
        SigmaDisplay = "± 118",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = 0",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "301.2 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "σ",
        Detail = "σ flat · frailty driver for hazard",
        Selected = true
    };

    public Measure Spoilage { get; init; } = new()
    {
        Display = "0.83 %/wk",
        SigmaDisplay = "± 0.11",
        SigmaKind = "λ(t|Z)",
        Detail = "marked point process · marks: vol, grade",
        Selected = true
    };

    [TreeName("grade @ intake")]
    public Measure GradeAtIntake { get; init; } = new()
    {
        Display = "EN590",
        Detail = "EN590 · Type B cert"
    };
}

public sealed class Tank02
{
    public Measure Level { get; init; } = new()
    {
        Display = "9,882 bbl",
        SigmaDisplay = "± 96",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · β = +14 (declared, Type B)",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "299.8 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "",
        Detail = "σ flat"
    };
}

// Plumbed in and reporting, but not yet uncertainty-characterised: readings with no σ behind them.
// This is the case the dashboard blanks, and the one a σ figure can be nominated for.
public sealed class Tank03
{
    [TreeNew]
    public Measure Level { get; init; } = new()
    {
        Display = "6,740 bbl",
        Detail = "no σ yet — awaiting Type A/B",
        IsNew = true
    };

    [TreeNew]
    public Measure Temp { get; init; } = new()
    {
        Display = "300.5 K",
        Detail = "no σ yet — awaiting Type A/B",
        IsNew = true
    };
}

public sealed class BerthDelivery
{
    [TreeTag("STEP 1")]
    public PumpA PumpA { get; init; } = new();

    [TreeTag("STEP 2")]
    public MeterStation Meter { get; init; } = new();
}

public sealed class PumpA;

public sealed class MeterStation
{
    public Measure Flow { get; init; } = new()
    {
        Display = "312 bbl/h",
        SigmaDisplay = "± 22",
        SigmaKind = "Σ aniso",
        Detail = "Σ anisotropic · error ellipse",
        IsVector = true
    };
}

public sealed class Intake
{
    public Measure Grade { get; init; } = new()
    {
        Display = "EN590",
        SigmaKind = "",
        Detail = "bound via contract"
    };
}
