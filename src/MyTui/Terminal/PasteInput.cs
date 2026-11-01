namespace MyTui.Terminal;

/// <summary>
/// Bracketed paste via libghostty-vt. The managed <c>Paste.Encode</c> passes the flag as a 4-byte
/// BOOL, and that call faults. The raw encoder is the same function with the flag as one byte.
/// </summary>
internal static class PasteInput
{
    internal static byte[] Encode(ReadOnlySpan<byte> data, bool bracketed)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        byte[] sourceBytes = data.ToArray();
        int capacity = sourceBytes.Length + 64;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] output = new byte[capacity];
            unsafe
            {
                fixed (byte* source = sourceBytes)
                fixed (byte* destination = output)
                {
                    nuint written;
                    Ghostty.Vt.Raw.GhosttyResult result = Ghostty.Vt.Raw.GhosttyVtNative.ghostty_paste_encode(
                        (sbyte*)source,
                        (nuint)sourceBytes.Length,
                        bracketed ? (byte)1 : (byte)0,
                        (sbyte*)destination,
                        (nuint)output.Length,
                        &written);
                    if (result == Ghostty.Vt.Raw.GhosttyResult.GHOSTTY_SUCCESS)
                    {
                        return output.AsSpan(0, checked((int)written)).ToArray();
                    }
                }
            }

            capacity = (data.Length * 2) + 64;
        }

        throw new IOException("paste encode failed");
    }
}
