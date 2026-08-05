// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// How the tour card lands: it is dropped from <see cref="DropHeight"/> above where it comes to
/// rest and bounces on that line like a ball, keeping <see cref="Bounce"/> of its speed each time
/// it hits. <see cref="FallSpeed"/> scales the pull, so the whole fall runs quicker or slower
/// without changing its shape.
/// </summary>
public static class TourSettings
{
    public const double DefaultDropHeight = 240;
    public const double DefaultBounce = 0.55;
    public const double DefaultFallSpeed = 1;

    public const double MinDropHeight = 0;
    public const double MaxDropHeight = 700;
    public const double MaxBounce = 0.85;
    public const double MinFallSpeed = 0.4;
    public const double MaxFallSpeed = 2.5;

    /// <summary>Pull at a fall speed of 1, in pixels per second squared.</summary>
    public const double BaseGravity = 4600;

    /// <summary>Below this the ball is done bouncing — anything less does not read as a bounce.</summary>
    public const double RestVelocity = 40;

    public static double DropHeight { get; private set; } = DefaultDropHeight;

    public static double Bounce { get; private set; } = DefaultBounce;

    public static double FallSpeed { get; private set; } = DefaultFallSpeed;

    public static double Gravity => BaseGravity * FallSpeed;

    public static event Action? Changed;

    public static void SetDropHeight(double value)
    {
        DropHeight = Math.Clamp(value, MinDropHeight, MaxDropHeight);
        Changed?.Invoke();
    }

    public static void SetBounce(double value)
    {
        Bounce = Math.Clamp(value, 0, MaxBounce);
        Changed?.Invoke();
    }

    public static void SetFallSpeed(double value)
    {
        FallSpeed = Math.Clamp(value, MinFallSpeed, MaxFallSpeed);
        Changed?.Invoke();
    }

    public static void ResetForTests()
    {
        DropHeight = DefaultDropHeight;
        Bounce = DefaultBounce;
        FallSpeed = DefaultFallSpeed;
        Changed = null;
    }
}
