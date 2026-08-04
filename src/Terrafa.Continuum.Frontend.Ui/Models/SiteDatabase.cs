// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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

    public Tank04 Tank04 { get; init; } = new();
    public Tank05 Tank05 { get; init; } = new();
    public Tank06 Tank06 { get; init; } = new();
    public Tank07 Tank07 { get; init; } = new();
    public Tank08 Tank08 { get; init; } = new();
    public Tank09 Tank09 { get; init; } = new();
    public Tank10 Tank10 { get; init; } = new();
    public Tank11 Tank11 { get; init; } = new();
    public Tank12 Tank12 { get; init; } = new();
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

public sealed class Tank04
{
    public Measure Level { get; init; } = new()
    {
        Display = "11,050 bbl",
        SigmaDisplay = "± 102",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = +3",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "300.1 K",
        SigmaDisplay = "± 0.3",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank05
{
    public Measure Level { get; init; } = new()
    {
        Display = "8,420 bbl",
        SigmaDisplay = "± 88",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = −6",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "298.6 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank06
{
    public Measure Level { get; init; } = new()
    {
        Display = "13,975 bbl",
        SigmaDisplay = "± 130",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = 0",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "301.8 K",
        SigmaDisplay = "± 0.5",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank07
{
    public Measure Level { get; init; } = new()
    {
        Display = "7,260 bbl",
        SigmaDisplay = "± 76",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = +9",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "297.9 K",
        SigmaDisplay = "± 0.3",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank08
{
    public Measure Level { get; init; } = new()
    {
        Display = "15,340 bbl",
        SigmaDisplay = "± 142",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = −11",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "302.4 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank09
{
    public Measure Level { get; init; } = new()
    {
        Display = "9,610 bbl",
        SigmaDisplay = "± 91",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = 0",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "299.2 K",
        SigmaDisplay = "± 0.3",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank10
{
    public Measure Level { get; init; } = new()
    {
        Display = "12,480 bbl",
        SigmaDisplay = "± 115",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = +5",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "300.7 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank11
{
    public Measure Level { get; init; } = new()
    {
        Display = "6,890 bbl",
        SigmaDisplay = "± 71",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = −4",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "298.1 K",
        SigmaDisplay = "± 0.3",
        SigmaKind = "σ",
        Detail = "σ flat"
    };
}

public sealed class Tank12
{
    public Measure Level { get; init; } = new()
    {
        Display = "10,225 bbl",
        SigmaDisplay = "± 98",
        SigmaKind = "σ(x)",
        Detail = "σ(x) heteroscedastic · bias β = 0",
        Selected = true
    };

    public Measure Temp { get; init; } = new()
    {
        Display = "299.9 K",
        SigmaDisplay = "± 0.4",
        SigmaKind = "σ",
        Detail = "σ flat"
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
