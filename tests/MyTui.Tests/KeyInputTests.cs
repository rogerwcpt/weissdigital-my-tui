using Ghostty.Vt;
using Ghostty.Vt.Enums;
using Terminal.Gui.Input;
using MyTui.Terminal;
using VtTerminal = Ghostty.Vt.Terminal;

namespace MyTui.Tests;

public class KeyInputTests
{
    [Fact]
    public void CtrlC_IsTheEncoderByte()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.C.WithCtrl);
        Assert.Equal(new byte[] { 0x03 }, bytes);
    }

    [Fact]
    public void CtrlD_IsEndOfTransmission()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.D.WithCtrl);
        Assert.Equal(new byte[] { 0x04 }, bytes);
    }

    [Fact]
    public void ArrowUp_IsCursorUp()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.CursorUp);
        Assert.Equal(new byte[] { 0x1b, (byte)'[', (byte)'A' }, bytes);
    }

    [Fact]
    public void ArrowUp_FollowsApplicationCursorMode()
    {
        using var terminal = new VtTerminal(80, 24);
        terminal.ModeSet(TerminalMode.CursorKeys, true);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.CursorUp);
        Assert.Equal(new byte[] { 0x1b, (byte)'O', (byte)'A' }, bytes);
    }

    [Fact]
    public void Escape_IsASingleByte()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.Esc);
        Assert.Equal(new byte[] { 0x1b }, bytes);
    }

    [Fact]
    public void Letter_IsItself()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        byte[]? bytes = KeyInput.Encode(terminal, encoder, Key.A);
        Assert.Equal(new byte[] { (byte)'a' }, bytes);
    }

    [Fact]
    public void RepeatedLetters_DoNotCollapseToNul()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        foreach (Key key in new[] { Key.E, Key.C, Key.H, Key.O, Key.E })
        {
            byte[]? bytes = KeyInput.Encode(terminal, encoder, key);
            Assert.NotNull(bytes);
            Assert.Equal((byte)char.ToLowerInvariant(key.ToString()[0]), bytes[0]);
            Assert.DoesNotContain((byte)0, bytes);
        }
    }

    [Fact]
    public void ShellChordsReachTheChild()
    {
        using var terminal = new VtTerminal(80, 24);
        using var encoder = new KeyEncoder();
        Assert.Equal(new byte[] { 0x11 }, KeyInput.Encode(terminal, encoder, Key.Q.WithCtrl));
        Assert.Equal(new byte[] { 0x0e }, KeyInput.Encode(terminal, encoder, Key.N.WithCtrl));
        Assert.Equal(new byte[] { 0x0f }, KeyInput.Encode(terminal, encoder, Key.O.WithCtrl));
        Assert.Equal(new byte[] { 0x12 }, KeyInput.Encode(terminal, encoder, Key.R.WithCtrl));
        Assert.Equal(new byte[] { 0x05 }, KeyInput.Encode(terminal, encoder, Key.E.WithCtrl));
    }
}
