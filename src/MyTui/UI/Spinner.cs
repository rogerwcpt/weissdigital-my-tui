namespace MyTui.UI;

/// <summary>
/// One-cell spinner for a session that is executing a command.
/// Frames advance on wall time, so a 30 Hz tick and a faster loop show the same motion.
/// </summary>
internal static class Spinner
{
    /// <summary>A line turning clockwise. Each frame is one column.</summary>
    internal const string Frames = "|/-\\";

    internal const long FrameMilliseconds = 120;

    internal static char Glyph(long now)
    {
        long frame = now < 0 ? 0 : now / FrameMilliseconds;
        return Frames[(int)(frame % Frames.Length)];
    }

    internal static string Mark(string text, bool busy, long now) =>
        busy ? text + " " + Glyph(now) : text;
}
