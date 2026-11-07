using MyTui.UI;

namespace MyTui.Tests;

public class WakePaceTests
{
    [Fact]
    public void AQuietScreenIdles()
    {
        WakeDecision decision = WakePace.Choose(now: 5_000, busy: false, fastUntil: 0);

        Assert.Equal(WakePace.IdlePerSecond, decision.PerSecond);
        Assert.Equal(0, decision.FastUntil);
    }

    [Fact]
    public void InputOrOutputRaisesTheRateAndHoldsIt()
    {
        WakeDecision busy = WakePace.Choose(now: 5_000, busy: true, fastUntil: 0);

        Assert.Equal(WakePace.ActivePerSecond, busy.PerSecond);
        Assert.Equal(5_000 + WakePace.TailMilliseconds, busy.FastUntil);

        WakeDecision held = WakePace.Choose(now: busy.FastUntil - 1, busy: false, fastUntil: busy.FastUntil);

        Assert.Equal(WakePace.ActivePerSecond, held.PerSecond);
        Assert.Equal(busy.FastUntil, held.FastUntil);
    }

    [Fact]
    public void AnotherEventRestartsTheHoldFromNow()
    {
        WakeDecision first = WakePace.Choose(now: 1_000, busy: true, fastUntil: 0);

        WakeDecision again = WakePace.Choose(now: 1_100, busy: true, fastUntil: first.FastUntil);

        Assert.Equal(WakePace.ActivePerSecond, again.PerSecond);
        Assert.Equal(1_100 + WakePace.TailMilliseconds, again.FastUntil);
    }

    [Fact]
    public void TheHoldEndsOnTime()
    {
        long until = 5_000 + WakePace.TailMilliseconds;

        WakeDecision idle = WakePace.Choose(now: until, busy: false, fastUntil: until);

        Assert.Equal(WakePace.IdlePerSecond, idle.PerSecond);
        Assert.Equal(until, idle.FastUntil);
    }

    [Fact]
    public void TheIdleCapWakesFarLessOften()
    {
        Assert.True(WakePace.IdlePerSecond * 3 <= WakePace.ActivePerSecond);
        Assert.InRange(WakePace.TailMilliseconds, 100, 500);
    }

    [Fact]
    public void TheMainLoopExposesItsInputQueue()
    {
        Assert.True(PendingInput.CanSeeQueue);
    }
}
