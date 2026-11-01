using System.Text;

namespace MyTui.Terminal;

/// <summary>
/// Wheel bytes for a program that tracks the mouse, and arrow keys for alternate-screen scroll.
/// Ghostty's mouse encoder returns a span into stack memory, so the bytes are built here.
/// </summary>
internal static class MouseInput
{
    internal static byte[]? Wheel(bool up, int column, int row, bool sgr)
    {
        int x = column + 1;
        int y = row + 1;
        int button = up ? 64 : 65;
        if (sgr)
        {
            return Encoding.ASCII.GetBytes($"\u001b[<{button};{x};{y}M");
        }

        if (x > 223 || y > 223)
        {
            return null;
        }

        return [0x1b, (byte)'[', (byte)'M', (byte)(button + 32), (byte)(x + 32), (byte)(y + 32)];
    }

    internal static byte[] Arrows(bool up, int count, bool applicationCursor)
    {
        byte letter = up ? (byte)'A' : (byte)'B';
        byte[] one = applicationCursor
            ? [0x1b, (byte)'O', letter]
            : [0x1b, (byte)'[', letter];
        var all = new byte[one.Length * count];
        for (int i = 0; i < count; i++)
        {
            one.CopyTo(all, i * one.Length);
        }

        return all;
    }
}
