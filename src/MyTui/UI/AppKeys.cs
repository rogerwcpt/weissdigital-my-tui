using System.Text;
using System.Text.Json;
using MyTui.Persistence;
using Terminal.Gui.Input;

namespace MyTui.UI;

internal enum AppCommand
{
    Menu,
    NewSession,
    CycleFocus,
    NextPanel,
    PreviousPanel,
    HideSessions,
    Maximize,
    CycleTab,
    Preview,
    Edit,
    Refresh,
    Templates,
    Close,
    Copy,
    Paste,
    Help,
    Quit,
}

internal enum KeyRoute
{
    Forward,
    Consumed,
    Run,
}

/// <summary>Ctrl+B (or the configured leader) is waiting for the command key.</summary>
internal sealed class LeaderState
{
    internal const long TimeoutMilliseconds = 3000;

    internal bool Armed { get; private set; }

    internal long ArmedAt { get; private set; }

    internal void Arm(long now)
    {
        Armed = true;
        ArmedAt = now;
    }

    internal void Clear() => Armed = false;

    internal bool IsArmed(long now)
    {
        if (!Armed)
        {
            return false;
        }

        if (now - ArmedAt > TimeoutMilliseconds)
        {
            Armed = false;
            return false;
        }

        return true;
    }
}

/// <summary>
/// One row of the key table. Routing, help, and the leader hint all come from <see cref="AppKeys.All"/>.
/// </summary>
internal sealed record AppBinding(
    AppCommand Command,
    string Label,
    string AfterLeaderText,
    char? AfterChar,
    bool AfterTab,
    bool AfterShiftTab,
    Key? Direct,
    string DirectText,
    bool InTerminal);

/// <summary>
/// App commands. Named AppKeys because KeyBindings is Terminal.Gui's type.
/// Inside a live terminal only the leader, copy, paste, and the scroll keys stay here.
/// </summary>
internal static class AppKeys
{
    internal static Key Leader { get; private set; } = Key.B.WithCtrl;

    internal static string LeaderText { get; private set; } = "Ctrl+B";

    internal static string LeaderHint =>
        $"{LeaderText}: n new  o list  z max  t tab  p preview  x close  ? keys  q quit";

    internal static readonly AppBinding[] All =
    [
        new(AppCommand.Menu, "commands", "m", 'm', false, false, Key.F9, "F9", true),
        new(AppCommand.NewSession, "new session", "n", 'n', false, false, Key.N.WithCtrl, "Ctrl+N", false),
        new(AppCommand.CycleFocus, "list, tab, or preview", "o", 'o', false, false, Key.O.WithCtrl, "Ctrl+O", false),
        new(AppCommand.NextPanel, "next panel", "Tab", null, true, false, Key.Tab.WithCtrl, "Ctrl+Tab", false),
        new(AppCommand.PreviousPanel, "previous panel", "Shift+Tab", null, false, true, Key.Tab.WithCtrl.WithShift, "Ctrl+Shift+Tab", false),
        new(AppCommand.HideSessions, "hide or show Sessions", "s", 's', false, false, Key.F4, "F4", false),
        new(AppCommand.Maximize, "maximize or restore", "z", 'z', false, false, Key.F11, "F11", false),
        new(AppCommand.CycleTab, "Terminal or Files", "t", 't', false, false, Key.T.WithCtrl, "Ctrl+T", false),
        new(AppCommand.Preview, "open or close the preview", "p", 'p', false, false, Key.P.WithCtrl, "Ctrl+P", false),
        new(AppCommand.Edit, "edit the session", "e", 'e', false, false, Key.E.WithCtrl, "Ctrl+E", false),
        new(AppCommand.Refresh, "refresh Files", "r", 'r', false, false, Key.R.WithCtrl, "Ctrl+R", false),
        new(AppCommand.Templates, "templates", "T", 'T', false, false, Key.F2, "F2", false),
        new(AppCommand.Close, "close the session", "x", 'x', false, false, Key.Delete, "Delete", false),
        new(AppCommand.Copy, "copy visible text", "c", 'c', false, false, Key.C.WithCtrl.WithShift, "Ctrl+Shift+C", true),
        new(AppCommand.Paste, "paste", "v", 'v', false, false, Key.V.WithCtrl.WithShift, "Ctrl+Shift+V", true),
        new(AppCommand.Help, "keystrokes", "?", '?', false, false, null, "", false),
        new(AppCommand.Quit, "quit", "q", 'q', false, false, Key.Q.WithCtrl, "Ctrl+Q", false),
    ];

    internal static void SetLeader(Key key, string text)
    {
        Leader = key;
        LeaderText = text;
    }

    /// <summary>Reads <c>leader</c> from <c>~/.config/my-tui/config.json</c> when it is present and valid.</summary>
    internal static void Load()
    {
        string path = Path.Combine(Path.GetDirectoryName(StateStore.DefaultPath()) ?? "", "config.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("leader", out JsonElement leader)
                && leader.ValueKind == JsonValueKind.String
                && leader.GetString() is string text
                && TryParseLeader(text, out Key key, out string label))
            {
                SetLeader(key, label);
            }
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
            // A JSON array or other non-object keeps Ctrl+B.
        }
        catch (IOException)
        {
        }
    }

    internal static bool TryParseLeader(string text, out Key key, out string label)
    {
        key = Key.Empty;
        label = "";
        string trimmed = text.Trim().ToLowerInvariant();
        if (trimmed is "ctrl+space" or "ctrl+ ")
        {
            key = Key.Space.WithCtrl;
            label = "Ctrl+Space";
            return true;
        }

        if (trimmed is "ctrl+\\" or "ctrl+backslash")
        {
            key = new Key('\\').WithCtrl;
            label = "Ctrl+\\";
            return true;
        }

        const string prefix = "ctrl+";
        if (trimmed.Length == prefix.Length + 1 && trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            char letter = trimmed[^1];
            if (letter is < 'a' or > 'z')
            {
                return false;
            }

            key = CtrlLetter(letter);
            label = "Ctrl+" + char.ToUpperInvariant(letter);
            return true;
        }

        return false;
    }

    internal static KeyRoute Route(Key key, bool inTerminal, LeaderState leader, long now, out AppCommand command)
    {
        command = default;
        if (leader.IsArmed(now))
        {
            leader.Clear();
            if (key == Leader)
            {
                return KeyRoute.Forward;
            }

            foreach (AppBinding binding in All)
            {
                if (!MatchesAfter(key, binding))
                {
                    continue;
                }

                command = binding.Command;
                return KeyRoute.Run;
            }

            return KeyRoute.Consumed;
        }

        if (key == Leader)
        {
            leader.Arm(now);
            return KeyRoute.Consumed;
        }

        // F9 is the Norton Commander menu key, and Terminal.app delivers it. Ctrl+Shift+P
        // stays as well for hosts that pass it through.
        if ((key == Key.F9 && !key.IsCtrl && !key.IsAlt && !key.IsShift)
            || (key == Key.P.WithCtrl.WithShift && !key.IsAlt))
        {
            command = AppCommand.Menu;
            return KeyRoute.Run;
        }

        foreach (AppBinding binding in All)
        {
            if (binding.Direct is not Key direct || key != direct)
            {
                continue;
            }

            if (inTerminal && !binding.InTerminal)
            {
                return KeyRoute.Forward;
            }

            command = binding.Command;
            return KeyRoute.Run;
        }

        return KeyRoute.Forward;
    }

    internal static IReadOnlyList<string> HelpLines()
    {
        var lines = new List<string>
        {
            $"{LeaderText} then a key inside a terminal. The direct key works outside one.",
            $"{LeaderText} twice    send {LeaderText} to the program",
        };
        foreach (AppBinding binding in All)
        {
            string direct = binding.DirectText.Length == 0 ? "" : "   " + binding.DirectText;
            lines.Add($"{LeaderText} {binding.AfterLeaderText,-10} {binding.Label}{direct}");
        }

        lines.Add("Shift+PgUp     scroll up, also in a terminal");
        lines.Add("Shift+PgDn     scroll down, also in a terminal");
        lines.Add("Shift+Home     top of the history");
        lines.Add("Shift+End      back to the prompt");
        lines.Add("+              new session");
        lines.Add("×              close the active session");
        lines.Add("●              unseen output in another session");
        lines.Add("!              a bell in another session");
        return lines;
    }

    private static bool MatchesAfter(Key key, AppBinding binding)
    {
        if (key.IsCtrl || key.IsAlt)
        {
            return false;
        }

        if (binding.AfterTab)
        {
            return key == Key.Tab;
        }

        if (binding.AfterShiftTab)
        {
            return key == Key.Tab.WithShift;
        }

        if (binding.AfterChar is not char expected || !TryLeaderChar(key, out char actual))
        {
            return false;
        }

        return actual == expected;
    }

    /// <summary>
    /// Leader letters match the character. Shift+/ is '?' on a US keyboard, and Terminal.Gui still
    /// reports the rune '/'. A shifted letter may arrive as 't' with Shift set, or as 'T'.
    /// </summary>
    private static bool TryLeaderChar(Key key, out char actual)
    {
        actual = default;
        if (!key.TryGetPrintableRune(out Rune rune) || !rune.IsBmp)
        {
            return false;
        }

        actual = (char)rune.Value;
        if (key.IsShift && actual == '/')
        {
            actual = '?';
        }
        else if (key.IsShift && actual is >= 'a' and <= 'z')
        {
            actual = char.ToUpperInvariant(actual);
        }

        return true;
    }

    private static Key CtrlLetter(char letter)
    {
        int index = letter - 'a';
        Key[] letters =
        [
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
            Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
        ];
        return letters[index].WithCtrl;
    }
}
