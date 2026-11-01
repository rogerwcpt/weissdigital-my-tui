namespace MyTui.UI;

/// <summary>
/// When the grid repaints after its session changed. The first change after a quiet spell paints
/// at once so an echo is not held back. Steady output paints at most once a frame. While the child
/// holds synchronized output (CSI ? 2026 h) the half-written screen is not shown, up to a limit.
/// </summary>
internal sealed class FramePacer
{
    internal const long FrameMilliseconds = 16;

    /// <summary>A child that never sends CSI ? 2026 l still gets painted this often.</summary>
    internal const long SyncHoldMilliseconds = 200;

    private long _paintedAt = -FrameMilliseconds;
    private long? _heldSince;

    /// <summary>Call only while the session has unpainted changes.</summary>
    internal bool ShouldPaint(long now, bool synchronized)
    {
        if (now - _paintedAt < FrameMilliseconds)
        {
            return false;
        }

        if (synchronized)
        {
            _heldSince ??= now;
            if (now - _heldSince.Value < SyncHoldMilliseconds)
            {
                return false;
            }
        }

        _heldSince = null;
        _paintedAt = now;
        return true;
    }
}
