using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public sealed class BubbleSpring
{
    private const double MaxStableStepSeconds = 0.004;
    private const double MaxAdvanceSeconds = 0.1;

    public double Position { get; set; } = 1;
    public double Velocity { get; set; }
    public double Target { get; set; } = 1;
    public double Frequency { get; set; } = 26;
    public double Damping { get; set; } = 0.5;

    public void Advance(double elapsedSeconds)
    {
        var remaining = Math.Clamp(elapsedSeconds, 0, MaxAdvanceSeconds);
        while (remaining > 0)
        {
            var step = Math.Min(remaining, MaxStableStepSeconds);
            var acceleration = -Frequency * Frequency * (Position - Target) - 2 * Damping * Frequency * Velocity;
            Velocity += acceleration * step;
            Position += Velocity * step;
            remaining -= step;
        }
    }

    public bool IsWithin(double positionEpsilon, double velocityEpsilon) =>
        Math.Abs(Position - Target) <= positionEpsilon && Math.Abs(Velocity) <= velocityEpsilon;

    public void Rest(double position)
    {
        Position = position;
        Target = position;
        Velocity = 0;
    }
}

public readonly record struct PopImpulse(double Velocity, double Frequency, double Damping);

public static class BubblePhysics
{
    public const double InflatedScale = 1.0;
    public const double PoppedScale = 0.93;
    public const double PressContactScale = 0.972;
    public const double FullHoldContactScale = 0.955;
    public const double WidthFollowRatio = 0.55;
    public const double PressDamping = 1;

    public static double MaxHoldSeconds => BubbleSettings.HoldSeconds;

    public static double PressFrequency => 45 * BubbleSettings.PopSpeed;

    public static double InflateFrequency => 22 * BubbleSettings.PopSpeed;

    public static double InflateDamping => 0.7 * WobbleDampingScale;

    public static double InflateKick => 1.2 * BubbleSettings.PopSpeed;

    public static double PressureFor(double heldSeconds) =>
        Math.Clamp(heldSeconds / MaxHoldSeconds, 0, 1);

    public static double ContactScaleFor(double pressure) =>
        PressContactScale + (FullHoldContactScale - PressContactScale) * Math.Clamp(pressure, 0, 1);

    public static PopImpulse PopFor(double pressure)
    {
        var clamped = Math.Clamp(pressure, 0, 1);
        var speed = BubbleSettings.PopSpeed;
        return new PopImpulse(
            Velocity: -(1.2 + 3.4 * clamped) * speed * BubbleSettings.PopForce,
            Frequency: (24 + 14 * clamped) * speed,
            Damping: (0.46 - 0.06 * clamped) * WobbleDampingScale);
    }

    private static double WobbleDampingScale => BubbleSettings.Wobble <= 0.5
        ? 1.6 - 1.2 * BubbleSettings.Wobble
        : 1.55 - 1.1 * BubbleSettings.Wobble;
}
