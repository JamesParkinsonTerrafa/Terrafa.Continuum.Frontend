// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

public static class BubbleSettings
{
    public const double DefaultPopSpeed = 1;
    public const double DefaultPopForce = 1;
    public const double DefaultWobble = 0.5;
    public const double DefaultHoldSeconds = 0.7;

    public const double MinPopSpeed = 0.5;
    public const double MaxPopSpeed = 2.5;
    public const double MinPopForce = 0.5;
    public const double MaxPopForce = 2;
    public const double MinHoldSeconds = 0.3;
    public const double MaxHoldSeconds = 1.5;

    public static double PopSpeed { get; private set; } = DefaultPopSpeed;

    public static double PopForce { get; private set; } = DefaultPopForce;

    public static double Wobble { get; private set; } = DefaultWobble;

    public static double HoldSeconds { get; private set; } = DefaultHoldSeconds;

    public static event Action? Changed;

    public static void SetPopSpeed(double value)
    {
        PopSpeed = Math.Clamp(value, MinPopSpeed, MaxPopSpeed);
        Changed?.Invoke();
    }

    public static void SetPopForce(double value)
    {
        PopForce = Math.Clamp(value, MinPopForce, MaxPopForce);
        Changed?.Invoke();
    }

    public static void SetWobble(double value)
    {
        Wobble = Math.Clamp(value, 0, 1);
        Changed?.Invoke();
    }

    public static void SetHoldSeconds(double value)
    {
        HoldSeconds = Math.Clamp(value, MinHoldSeconds, MaxHoldSeconds);
        Changed?.Invoke();
    }
}
