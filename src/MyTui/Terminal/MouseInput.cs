using System.Text;

namespace MyTui.Terminal;

/// <summary>Press, release, or motion. A click the driver splits into press then release uses both.</summary>
internal enum PointerKind
{
    Press,
    Release,
    Motion,
}

/// <summary>
/// Wheel, pointer, and arrow bytes for a program. Ghostty's mouse encoder returns a span into
/// stack memory, so the bytes are built here.
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

    /// <summary>
    /// One pointer report. <paramref name="button"/> is 0 left, 1 middle, 2 right, or 3 when
    /// nothing is held. Column and row are 0-based. SGR is mode 1006 (and 1016, as cells).
    /// URXVT is 1015. UTF-8 is 1005. Otherwise the report is X10.
    /// </summary>
    internal static byte[]? Pointer(
        int column,
        int row,
        int button,
        bool release,
        bool motion,
        int modifiers,
        bool sgr,
        bool urxvt,
        bool utf8)
    {
        int x = column + 1;
        int y = row + 1;
        if (x < 1 || y < 1)
        {
            return null;
        }

        // X10 names every release as button 3. SGR keeps the button and ends with m.
        int code = release && !sgr ? 3 : button;
        code += modifiers;
        if (motion)
        {
            code += 32;
        }

        if (sgr)
        {
            char end = release ? 'm' : 'M';
            return Encoding.ASCII.GetBytes($"\u001b[<{code};{x};{y}{end}");
        }

        if (urxvt)
        {
            return Encoding.ASCII.GetBytes($"\u001b[{code + 32};{x + 32};{y + 32}M");
        }

        int cb = code + 32;
        int cx = x + 32;
        int cy = y + 32;
        if (utf8)
        {
            return Utf8Report(cb, cx, cy);
        }

        if (cb > 255 || cx > 255 || cy > 255)
        {
            return null;
        }

        return [0x1b, (byte)'[', (byte)'M', (byte)cb, (byte)cx, (byte)cy];
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

    private static byte[] Utf8Report(int cb, int cx, int cy)
    {
        var buffer = new List<byte> { 0x1b, (byte)'[', (byte)'M' };
        AppendCode(buffer, cb);
        AppendCode(buffer, cx);
        AppendCode(buffer, cy);
        return buffer.ToArray();
    }

    private static void AppendCode(List<byte> buffer, int value)
    {
        if (value < 0x80)
        {
            buffer.Add((byte)value);
            return;
        }

        buffer.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(value)));
    }
}
