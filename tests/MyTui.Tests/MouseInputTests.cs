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
    public void SgrPressReleaseAndDragUseTheButton()
    {
        Assert.Equal("\u001b[<0;1;5M"u8.ToArray(), MouseInput.Pointer(0, 4, 0, false, false, 0, true, false, false));
        Assert.Equal("\u001b[<0;1;5m"u8.ToArray(), MouseInput.Pointer(0, 4, 0, true, false, 0, true, false, false));
        Assert.Equal("\u001b[<32;3;4M"u8.ToArray(), MouseInput.Pointer(2, 3, 0, false, true, 0, true, false, false));
        Assert.Equal("\u001b[<35;1;1M"u8.ToArray(), MouseInput.Pointer(0, 0, 3, false, true, 0, true, false, false));
        Assert.Equal("\u001b[<16;1;1M"u8.ToArray(), MouseInput.Pointer(0, 0, 0, false, false, 16, true, false, false));
    }

    [Fact]
    public void X10ReleaseIsButtonThreeAndStopsPastColumn223()
    {
        Assert.Equal(
            new byte[] { 0x1b, (byte)'[', (byte)'M', 32, 33, 34 },
            MouseInput.Pointer(0, 1, 0, false, false, 0, false, false, false));
        Assert.Equal(
            new byte[] { 0x1b, (byte)'[', (byte)'M', 35, 33, 34 },
            MouseInput.Pointer(0, 1, 0, true, false, 0, false, false, false));
        Assert.Null(MouseInput.Pointer(223, 0, 0, false, false, 0, false, false, false));
    }

    [Fact]
    public void UrxvtAndUtf8UseTheirOwnShapes()
    {
        Assert.Equal("\u001b[32;33;33M"u8.ToArray(), MouseInput.Pointer(0, 0, 0, false, false, 0, false, true, false));
        byte[] utf8 = MouseInput.Pointer(200, 0, 0, false, false, 0, false, false, true)!;
        Assert.Equal(new byte[] { 0x1b, (byte)'[', (byte)'M', 32, 0xc3, 0xa9, 33 }, utf8);
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
