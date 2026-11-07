namespace MyTui.UI;

/// <summary>
/// How often the main loop may wake. Terminal.Gui sleeps out the rest of each iteration and
/// does not end that sleep for a key or PTY output. The cap is chosen at the start of the
/// iteration, which is before the sleep is measured. A quiet screen wakes at
/// <see cref="IdlePerSecond"/>. A queued key or unpainted output wakes at
/// <see cref="ActivePerSecond"/>, and stays there for <see cref="TailMilliseconds"/> so a
/// burst of typing or lumpy output does not fall back between events.
/// </summary>
internal static class WakePace
{
    /// <summary>About 8 ms. Matches the rate that removed the old typing lag.</summary>
    internal const ushort ActivePerSecond = 120;

    /// <summary>About 33 ms. The first event of a quiet spell waits at most one of these.</summary>
    internal const ushort IdlePerSecond = 30;

    /// <summary>Stay on the fast cap after the last key or output, then return to idle.</summary>
    internal const long TailMilliseconds = 250;

    internal static WakeDecision Choose(long now, bool busy, long fastUntil)
    {
        if (busy)
        {
            fastUntil = now + TailMilliseconds;
        }

        ushort perSecond = now < fastUntil ? ActivePerSecond : IdlePerSecond;
        return new WakeDecision(perSecond, fastUntil);
    }
}

internal readonly record struct WakeDecision(ushort PerSecond, long FastUntil);
