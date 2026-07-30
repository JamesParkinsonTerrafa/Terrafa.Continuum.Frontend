using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Tests;

public class BubblePhysicsTests
{
    private const double FrameSeconds = 1.0 / 480;

    private sealed record PopRun(double Trough, double TroughSeconds, double SettleSeconds);

    private static PopRun RunPop(double pressure)
    {
        var impulse = BubblePhysics.PopFor(pressure);
        var spring = new BubbleSpring
        {
            Position = BubblePhysics.ContactScaleFor(pressure),
            Target = BubblePhysics.PoppedScale,
            Frequency = impulse.Frequency,
            Damping = impulse.Damping,
            Velocity = impulse.Velocity
        };

        var trough = spring.Position;
        var troughSeconds = 0.0;
        var elapsed = 0.0;
        while (elapsed < 2)
        {
            spring.Advance(FrameSeconds);
            elapsed += FrameSeconds;
            if (spring.Position < trough)
            {
                trough = spring.Position;
                troughSeconds = elapsed;
            }
            if (spring.IsWithin(0.002, 0.05)) return new PopRun(trough, troughSeconds, elapsed);
        }
        return new PopRun(trough, troughSeconds, elapsed);
    }

    [Fact]
    public void PopSettlesAtPoppedScale()
    {
        var run = RunPop(0.2);
        Assert.True(run.SettleSeconds < 0.6, $"settled in {run.SettleSeconds:0.###}s");
    }

    [Fact]
    public void PopUndershootsBelowSettleHeight()
    {
        var run = RunPop(0.2);
        Assert.True(run.Trough < BubblePhysics.PoppedScale - 0.02,
            $"trough {run.Trough:0.###} barely dips below {BubblePhysics.PoppedScale}");
    }

    [Fact]
    public void MorePressurePopsDeeperAndFaster()
    {
        var tap = RunPop(0.1);
        var held = RunPop(1);
        Assert.True(held.Trough < tap.Trough,
            $"held trough {held.Trough:0.###} vs tap trough {tap.Trough:0.###}");
        Assert.True(held.TroughSeconds < tap.TroughSeconds,
            $"held reached trough in {held.TroughSeconds:0.###}s vs tap {tap.TroughSeconds:0.###}s");
    }

    [Fact]
    public void PressureClampsAtMaxHold()
    {
        Assert.Equal(1, BubblePhysics.PressureFor(BubblePhysics.MaxHoldSeconds * 3));
        Assert.Equal(0, BubblePhysics.PressureFor(-1));
        Assert.Equal(BubblePhysics.PopFor(1), BubblePhysics.PopFor(5));
    }

    [Fact]
    public void LongerHoldCompressesFurtherBeforeThePop()
    {
        Assert.True(BubblePhysics.ContactScaleFor(1) < BubblePhysics.ContactScaleFor(0));
        Assert.True(BubblePhysics.ContactScaleFor(0) < BubblePhysics.InflatedScale);
    }

    [Fact]
    public void AdvanceStaysStableThroughLargeFrameGaps()
    {
        var impulse = BubblePhysics.PopFor(1);
        var spring = new BubbleSpring
        {
            Position = BubblePhysics.ContactScaleFor(1),
            Target = BubblePhysics.PoppedScale,
            Frequency = impulse.Frequency,
            Damping = impulse.Damping,
            Velocity = impulse.Velocity
        };
        for (var frame = 0; frame < 30; frame++)
        {
            spring.Advance(0.5);
            Assert.InRange(spring.Position, 0.5, 1.5);
        }
        Assert.True(spring.IsWithin(0.002, 0.05));
    }

    [Fact]
    public void PopSpeedCompressesTimeWithoutChangingDepth()
    {
        var baseline = RunPop(1);
        BubbleSettings.SetPopSpeed(2);
        try
        {
            var fast = RunPop(1);
            Assert.True(fast.TroughSeconds < baseline.TroughSeconds * 0.65,
                $"trough at {fast.TroughSeconds:0.###}s vs baseline {baseline.TroughSeconds:0.###}s");
            Assert.True(Math.Abs(fast.Trough - baseline.Trough) < 0.005,
                $"trough {fast.Trough:0.###} drifted from baseline {baseline.Trough:0.###}");
        }
        finally
        {
            BubbleSettings.SetPopSpeed(BubbleSettings.DefaultPopSpeed);
        }
    }

    [Fact]
    public void PopForceDeepensTheTrough()
    {
        var baseline = RunPop(1);
        BubbleSettings.SetPopForce(2);
        try
        {
            var forced = RunPop(1);
            Assert.True(forced.Trough < baseline.Trough - 0.02,
                $"forced trough {forced.Trough:0.###} vs baseline {baseline.Trough:0.###}");
        }
        finally
        {
            BubbleSettings.SetPopForce(BubbleSettings.DefaultPopForce);
        }
    }

    [Fact]
    public void WobbleLowersDampingAndDefaultMatchesTunedFeel()
    {
        try
        {
            BubbleSettings.SetWobble(BubbleSettings.DefaultWobble);
            Assert.Equal(0.40, BubblePhysics.PopFor(1).Damping, 10);
            BubbleSettings.SetWobble(0);
            var stiff = BubblePhysics.PopFor(1).Damping;
            BubbleSettings.SetWobble(1);
            var springy = BubblePhysics.PopFor(1).Damping;
            Assert.True(springy < stiff, $"wobble 1 damping {springy:0.###} vs wobble 0 {stiff:0.###}");
        }
        finally
        {
            BubbleSettings.SetWobble(BubbleSettings.DefaultWobble);
        }
    }

    [Fact]
    public void HoldSecondsRescalesPressureBuildup()
    {
        BubbleSettings.SetHoldSeconds(1.4);
        try
        {
            Assert.Equal(0.5, BubblePhysics.PressureFor(0.7), 10);
        }
        finally
        {
            BubbleSettings.SetHoldSeconds(BubbleSettings.DefaultHoldSeconds);
        }
    }

    [Fact]
    public void InflateFromPoppedOvershootsThenSettlesAtFullHeight()
    {
        var spring = new BubbleSpring
        {
            Position = BubblePhysics.PoppedScale,
            Target = BubblePhysics.InflatedScale,
            Frequency = BubblePhysics.InflateFrequency,
            Damping = BubblePhysics.InflateDamping,
            Velocity = BubblePhysics.InflateKick
        };
        var peak = spring.Position;
        var elapsed = 0.0;
        while (elapsed < 2 && !spring.IsWithin(0.002, 0.05))
        {
            spring.Advance(FrameSeconds);
            elapsed += FrameSeconds;
            peak = Math.Max(peak, spring.Position);
        }
        Assert.True(peak > BubblePhysics.InflatedScale, $"peak {peak:0.###} never puffed past 1");
        Assert.True(peak < 1.05, $"peak {peak:0.###} balloons too far");
        Assert.True(elapsed < 0.6, $"settled in {elapsed:0.###}s");
    }
}
