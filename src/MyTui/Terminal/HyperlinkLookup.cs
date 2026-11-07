using System.Reflection;
using System.Text;
using Ghostty.Vt.Raw;

namespace MyTui.Terminal;

/// <summary>
/// OSC 8 target for one cell. The managed grid ref exposes the flag, not the URI.
/// </summary>
internal static class HyperlinkLookup
{
    private static readonly FieldInfo? NativeField =
        typeof(Ghostty.Vt.GridRef).GetField("Native", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly Type? NativeType =
        typeof(Ghostty.Vt.Terminal).Assembly.GetType("Ghostty.Vt.Native.GhosttyGridRefNative");

    private static readonly FieldInfo? SizeField = NativeType?.GetField("Size", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? NodeField = NativeType?.GetField("Node", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? XField = NativeType?.GetField("X", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? YField = NativeType?.GetField("Y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static unsafe string? Read(Ghostty.Vt.Terminal terminal, int column, int row)
    {
        if (column < 0 || row < 0 || NativeField is null || SizeField is null || NodeField is null || XField is null || YField is null)
        {
            return null;
        }

        Ghostty.Vt.GridRef grid;
        try
        {
            grid = terminal.GetGridRef(Ghostty.Vt.Types.Point.Viewport(column, row));
            if (!grid.GetCell().HasHyperlink)
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        if (NativeField.GetValue(grid) is not object native
            || SizeField.GetValue(native) is not nuint size
            || NodeField.GetValue(native) is not IntPtr node
            || XField.GetValue(native) is not ushort x
            || YField.GetValue(native) is not ushort y)
        {
            return null;
        }

        var raw = new GhosttyGridRef
        {
            size = size,
            node = (void*)node,
            x = x,
            y = y,
        };
        byte* stack = stackalloc byte[1024];
        nuint written = 0;
        GhosttyResult result = GhosttyVtNative.ghostty_grid_ref_hyperlink_uri(&raw, stack, 1024, &written);
        if (result == GhosttyResult.GHOSTTY_OUT_OF_SPACE && written is > 1024 and <= 8192)
        {
            byte[] heap = new byte[(int)written];
            fixed (byte* bigger = heap)
            {
                result = GhosttyVtNative.ghostty_grid_ref_hyperlink_uri(&raw, bigger, written, &written);
                if (result == GhosttyResult.GHOSTTY_SUCCESS && written > 0 && written <= (nuint)heap.Length)
                {
                    return Decode(heap.AsSpan(0, (int)written));
                }
            }

            return null;
        }

        if (result != GhosttyResult.GHOSTTY_SUCCESS || written == 0 || written > 1024)
        {
            return null;
        }

        return Decode(new Span<byte>(stack, (int)written));
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0 && bytes[^1] is 0 or (byte)'\n' or (byte)'\r' or (byte)' ' or (byte)'\t')
        {
            bytes = bytes[..^1];
        }

        string url = Encoding.UTF8.GetString(bytes);
        return GridText.IsOpenable(url) ? url : "";
    }
}
