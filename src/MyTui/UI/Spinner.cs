namespace MyTui.UI;

/// <summary>
/// One-cell cursor pulse before the close column in the Sessions list.
/// Frames advance on wall time, so the pulse stays slow regardless of the loop rate.
/// </summary>
internal static class Spinner
{
    /// <summary>An underline, half-height block, filled cursor, then half-height block. Each frame is one column.</summary>
    internal const string Frames = "▁▄█▄";

    internal const long FrameMilliseconds = 700;

    internal static char Glyph(long now)
    {
        long frame = now < 0 ? 0 : now / FrameMilliseconds;
        return Frames[(int)(frame % Frames.Length)];
    }

    internal static string Mark(string text, bool busy, long now) =>
        busy ? text + " " + Glyph(now) : text;
}
