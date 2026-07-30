using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public sealed class BubbleKeyAnimator
{
    private enum BubbleState
    {
        Inflated,
        Pressing,
        Popping,
        Popped,
        PoppedPressing,
        PoppedSettling,
        Inflating
    }

    private const double SettlePositionEpsilon = 0.002;
    private const double SettleVelocityEpsilon = 0.05;
    private const double PoppedSquishScale = 0.916;
    private const double PressTensionStrength = 0.45;
    private const double ShadowSwapUpScale = 0.9545;
    private const double StrengthRampSeconds = 0.12;
    private const double MaxFrameSeconds = 1.0 / 30;
    private const double ProgrammaticPopPressure = 0.5;

    private readonly SquircleBorder key;
    private readonly ScaleTransform scaleTransform = new();
    private readonly BubbleSpring spring = new();
    private readonly Stopwatch holdClock = new();
    private readonly Stopwatch frameClock = new();

    private BubbleState state = BubbleState.Inflated;
    private IPointer? heldPointer;
    private double lastFrameSeconds;
    private double troughPosition;
    private double popStartPosition;
    private double popStartStrength;
    private double inflateStartPosition;
    private bool pressedClassActive;
    private double? localStrength;
    private bool frameLoopRunning;

    public event Action<double>? PopStarted;

    public BubbleKeyAnimator(SquircleBorder key)
    {
        this.key = key;
        key.RenderTransform = scaleTransform;
        key.PointerPressed += OnPointerPressed;
        key.PointerReleased += OnPointerReleased;
        key.PointerCaptureLost += OnPointerCaptureLost;
        key.AttachedToVisualTree += (_, _) => EnsureFrameLoop();
        spring.Rest(BubblePhysics.InflatedScale);
    }

    public bool IsPoppedOrPopping => state is BubbleState.Popping
        or BubbleState.Popped
        or BubbleState.PoppedPressing
        or BubbleState.PoppedSettling;

    internal double CurrentScale => spring.Position;

    public void RestPopped()
    {
        heldPointer = null;
        state = BubbleState.Popped;
        spring.Rest(BubblePhysics.PoppedScale);
        SetPressedClass(true);
        ClearStrength();
        ApplyScale();
    }

    public void RestInflated()
    {
        heldPointer = null;
        state = BubbleState.Inflated;
        spring.Rest(BubblePhysics.InflatedScale);
        SetPressedClass(false);
        ClearStrength();
        ApplyScale();
    }

    public void PopProgrammatic()
    {
        if (IsPoppedOrPopping) return;
        BeginPop(ProgrammaticPopPressure);
    }

    public void Inflate()
    {
        if (state is BubbleState.Inflated or BubbleState.Inflating or BubbleState.Pressing) return;
        BeginInflate();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (heldPointer is not null) return;
        if (!e.GetCurrentPoint(key).Properties.IsLeftButtonPressed) return;
        if (state is BubbleState.Popping or BubbleState.PoppedSettling) return;

        heldPointer = e.Pointer;
        e.Pointer.Capture(key);
        holdClock.Restart();

        if (state is BubbleState.Popped)
        {
            state = BubbleState.PoppedPressing;
            RetargetSpring(PoppedSquishScale, BubblePhysics.PressFrequency, BubblePhysics.PressDamping);
        }
        else
        {
            state = BubbleState.Pressing;
            RetargetSpring(
                BubblePhysics.ContactScaleFor(0), BubblePhysics.PressFrequency, BubblePhysics.PressDamping);
        }
        EnsureFrameLoop();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != heldPointer) return;
        heldPointer = null;
        var releasedInside = new Rect(key.Bounds.Size).Contains(e.GetPosition(key));

        if (state is BubbleState.Pressing)
        {
            if (releasedInside) BeginPop(BubblePhysics.PressureFor(holdClock.Elapsed.TotalSeconds));
            else BeginInflate();
        }
        else if (state is BubbleState.PoppedPressing)
        {
            BeginPoppedSettle();
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (heldPointer is null) return;
        heldPointer = null;
        if (state is BubbleState.Pressing) BeginInflate();
        else if (state is BubbleState.PoppedPressing) BeginPoppedSettle();
    }

    private void BeginPop(double pressure)
    {
        var impulse = BubblePhysics.PopFor(pressure);
        popStartPosition = Math.Max(spring.Position, BubblePhysics.PoppedScale + 0.02);
        popStartStrength = localStrength ?? key.ShadowStrength;
        troughPosition = spring.Position;
        state = BubbleState.Popping;
        spring.Target = BubblePhysics.PoppedScale;
        spring.Frequency = impulse.Frequency;
        spring.Damping = impulse.Damping;
        spring.Velocity = impulse.Velocity;
        EnsureFrameLoop();
        PopStarted?.Invoke(pressure);
    }

    internal void ContinuePop(double position, double pressure)
    {
        heldPointer = null;
        var impulse = BubblePhysics.PopFor(pressure);
        spring.Position = position;
        spring.Target = BubblePhysics.PoppedScale;
        spring.Frequency = impulse.Frequency;
        spring.Damping = impulse.Damping;
        spring.Velocity = impulse.Velocity;
        popStartPosition = Math.Max(position, BubblePhysics.PoppedScale + 0.02);
        troughPosition = position;
        var idleStrength = ButtonSettings.IdleEmbossStrength;
        popStartStrength = idleStrength + (PressTensionStrength - idleStrength) * pressure;
        state = BubbleState.Popping;
        SetPressedClass(false);
        SetStrength(popStartStrength);
        ApplyScale();
        EnsureFrameLoop();
    }

    private void BeginInflate()
    {
        state = BubbleState.Inflating;
        inflateStartPosition = spring.Position;
        spring.Target = BubblePhysics.InflatedScale;
        spring.Frequency = BubblePhysics.InflateFrequency;
        spring.Damping = BubblePhysics.InflateDamping;
        spring.Velocity = Math.Max(spring.Velocity, 0) + BubblePhysics.InflateKick;
        EnsureFrameLoop();
    }

    private void BeginPoppedSettle()
    {
        state = BubbleState.PoppedSettling;
        RetargetSpring(BubblePhysics.PoppedScale, BubblePhysics.InflateFrequency, 0.55);
        EnsureFrameLoop();
    }

    private void RetargetSpring(double target, double frequency, double damping)
    {
        spring.Target = target;
        spring.Frequency = frequency;
        spring.Damping = damping;
    }

    private void EnsureFrameLoop()
    {
        if (frameLoopRunning || !NeedsFrames) return;
        if (TopLevel.GetTopLevel(key) is not { } topLevel) return;
        frameLoopRunning = true;
        frameClock.Restart();
        lastFrameSeconds = 0;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private bool NeedsFrames => state is BubbleState.Pressing
        or BubbleState.PoppedPressing
        or BubbleState.Popping
        or BubbleState.PoppedSettling
        or BubbleState.Inflating;

    private void OnAnimationFrame(TimeSpan _)
    {
        frameLoopRunning = false;
        if (!NeedsFrames) return;

        var now = frameClock.Elapsed.TotalSeconds;
        var elapsed = Math.Clamp(now - lastFrameSeconds, 0.0001, MaxFrameSeconds);
        lastFrameSeconds = now;
        StepState(elapsed);

        if (NeedsFrames && TopLevel.GetTopLevel(key) is { } topLevel)
        {
            frameLoopRunning = true;
            topLevel.RequestAnimationFrame(OnAnimationFrame);
        }
    }

    private void StepState(double elapsed)
    {
        switch (state)
        {
            case BubbleState.Pressing:
                StepPressing(elapsed);
                break;
            case BubbleState.PoppedPressing:
            case BubbleState.PoppedSettling:
                StepPoppedMotion(elapsed);
                break;
            case BubbleState.Popping:
                StepPopping(elapsed);
                break;
            case BubbleState.Inflating:
                StepInflating(elapsed);
                break;
        }
    }

    private void StepPressing(double elapsed)
    {
        var pressure = BubblePhysics.PressureFor(holdClock.Elapsed.TotalSeconds);
        spring.Target = BubblePhysics.ContactScaleFor(pressure);
        spring.Advance(elapsed);
        ApplyScale();

        var idleStrength = ButtonSettings.IdleEmbossStrength;
        SetStrength(idleStrength + (PressTensionStrength - idleStrength) * pressure);

        if (pressure >= 1) BeginPop(1);
    }

    private void StepPoppedMotion(double elapsed)
    {
        spring.Advance(elapsed);
        ApplyScale();
        if (state is BubbleState.PoppedSettling && spring.IsWithin(SettlePositionEpsilon, SettleVelocityEpsilon))
        {
            RestPopped();
        }
    }

    private void StepPopping(double elapsed)
    {
        spring.Advance(elapsed);
        troughPosition = Math.Min(troughPosition, spring.Position);
        if (!pressedClassActive && spring.Position <= BubblePhysics.PoppedScale)
        {
            SetPressedClass(true);
            SetStrength(0);
        }
        UpdatePoppingStrength(elapsed);
        ApplyScale();

        if (spring.IsWithin(SettlePositionEpsilon, SettleVelocityEpsilon))
        {
            RestPopped();
        }
    }

    private void StepInflating(double elapsed)
    {
        spring.Advance(elapsed);
        if (pressedClassActive && spring.Position >= ShadowSwapUpScale)
        {
            SetPressedClass(false);
            SetStrength(0);
        }
        UpdateInflatingStrength(elapsed);
        ApplyScale();

        if (spring.IsWithin(SettlePositionEpsilon, SettleVelocityEpsilon))
        {
            RestInflated();
        }
    }

    private void UpdatePoppingStrength(double elapsed)
    {
        if (!pressedClassActive)
        {
            var travel = popStartPosition - BubblePhysics.PoppedScale;
            var remaining = Math.Clamp((spring.Position - BubblePhysics.PoppedScale) / travel, 0, 1);
            SetStrength(popStartStrength * remaining);
            return;
        }
        RampStrengthTowards(1, elapsed);
    }

    private void UpdateInflatingStrength(double elapsed)
    {
        if (pressedClassActive)
        {
            var travel = ShadowSwapUpScale - inflateStartPosition;
            if (travel <= 0.001) return;
            var progress = Math.Clamp((spring.Position - inflateStartPosition) / travel, 0, 1);
            SetStrength(1 - progress);
            return;
        }
        RampStrengthTowards(ButtonSettings.IdleEmbossStrength, elapsed);
    }

    private void RampStrengthTowards(double target, double elapsed)
    {
        var current = localStrength ?? key.ShadowStrength;
        var blend = Math.Min(1, elapsed / StrengthRampSeconds);
        SetStrength(current + (target - current) * blend);
    }

    private void SetPressedClass(bool pressed)
    {
        pressedClassActive = pressed;
        key.Classes.Set("emboss", !pressed);
        key.Classes.Set("emboss-press", pressed);
    }

    private void SetStrength(double strength)
    {
        localStrength = strength;
        key.ShadowStrength = strength;
    }

    private void ClearStrength()
    {
        localStrength = null;
        key.ClearValue(SquircleBorder.ShadowStrengthProperty);
    }

    private void ApplyScale()
    {
        scaleTransform.ScaleY = spring.Position;
        scaleTransform.ScaleX = 1 - (1 - spring.Position) * BubblePhysics.WidthFollowRatio;
    }
}
