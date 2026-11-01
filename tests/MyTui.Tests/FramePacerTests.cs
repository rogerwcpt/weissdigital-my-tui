using MyTui.UI;

namespace MyTui.Tests;

public class FramePacerTests
{
    [Fact]
    public void TheFirstChangePaintsAtOnce()
    {
        var pacer = new FramePacer();

        Assert.True(pacer.ShouldPaint(0, synchronized: false));
    }

    [Fact]
    public void AnEchoAfterAPausePaintsAtOnce()
    {
        var pacer = new FramePacer();
        Assert.True(pacer.ShouldPaint(1_000, synchronized: false));

        Assert.True(pacer.ShouldPaint(1_000 + FramePacer.FrameMilliseconds, synchronized: false));
    }

    [Fact]
    public void SteadyOutputPaintsOncePerFrame()
    {
        var pacer = new FramePacer();
        Assert.True(pacer.ShouldPaint(1_000, synchronized: false));

        Assert.False(pacer.ShouldPaint(1_008, synchronized: false));
        Assert.True(pacer.ShouldPaint(1_016, synchronized: false));
        Assert.False(pacer.ShouldPaint(1_020, synchronized: false));
    }

    [Fact]
    public void ASynchronizedUpdateIsNotPaintedHalfWritten()
    {
        var pacer = new FramePacer();

        Assert.False(pacer.ShouldPaint(1_000, synchronized: true));
        Assert.False(pacer.ShouldPaint(1_050, synchronized: true));
        Assert.True(pacer.ShouldPaint(1_060, synchronized: false));
    }

    [Fact]
    public void AChildThatNeverEndsTheUpdateIsStillPainted()
    {
        var pacer = new FramePacer();

        Assert.False(pacer.ShouldPaint(1_000, synchronized: true));
        Assert.False(pacer.ShouldPaint(1_000 + FramePacer.SyncHoldMilliseconds - 1, synchronized: true));
        Assert.True(pacer.ShouldPaint(1_000 + FramePacer.SyncHoldMilliseconds, synchronized: true));
    }

    [Fact]
    public void TheHoldStartsAgainAfterAForcedPaint()
    {
        var pacer = new FramePacer();
        Assert.False(pacer.ShouldPaint(1_000, synchronized: true));
        Assert.True(pacer.ShouldPaint(1_200, synchronized: true));

        Assert.False(pacer.ShouldPaint(1_216, synchronized: true));
        Assert.False(pacer.ShouldPaint(1_216 + FramePacer.SyncHoldMilliseconds - 1, synchronized: true));
        Assert.True(pacer.ShouldPaint(1_216 + FramePacer.SyncHoldMilliseconds, synchronized: true));
    }
}
