using MyTui.Terminal;

namespace MyTui.Tests;

public class MouseInputTests
{
    [Fact]
    public void SgrWheelIsOneBased()
    {
        byte[]? bytes = MouseInput.Wheel(up: true, column: 0, row: 4, sgr: true);
        Assert.Equal("\u001b[<64;1;5M"u8.ToArray(), bytes);
    }

    [Fact]
    public void X10OffsetsByThirtyTwo()
    {
        byte[]? bytes = MouseInput.Wheel(up: false, column: 1, row: 2, sgr: false);
        Assert.Equal(new byte[] { 0x1b, (byte)'[', (byte)'M', (byte)(65 + 32), (byte)(2 + 32), (byte)(3 + 32) }, bytes);
    }

    [Fact]
    public void X10StopsPastColumn223()
    {
        Assert.Null(MouseInput.Wheel(up: true, column: 223, row: 0, sgr: false));
        Assert.NotNull(MouseInput.Wheel(up: true, column: 223, row: 0, sgr: true));
    }

    [Fact]
    public void ArrowsFollowCursorKeyMode()
    {
        Assert.Equal(new byte[] { 0x1b, (byte)'[', (byte)'A', 0x1b, (byte)'[', (byte)'A', 0x1b, (byte)'[', (byte)'A' },
            MouseInput.Arrows(up: true, count: 3, applicationCursor: false));
        Assert.Equal(new byte[] { 0x1b, (byte)'O', (byte)'B' },
            MouseInput.Arrows(up: false, count: 1, applicationCursor: true));
    }
}
