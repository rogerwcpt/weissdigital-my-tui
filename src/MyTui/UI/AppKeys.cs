using Terminal.Gui.Input;

namespace MyTui.UI;

internal enum AppCommand
{
    Menu,
    Preview,
    Rename,
    CreateFolder,
    Delete,
    Quit,
    Copy,
    Paste,
    Center,
    Sessions,
}

/// <summary>
/// The few keys my-tui keeps for itself, even inside a live terminal. Host terminals and agent
/// CLIs swallow most chords, so most commands are in the F9 palette only. Rename, create-folder,
/// and delete are reserved only while Sessions or Files has focus. Session numbers are
/// Ctrl+Option+1 through Ctrl+Option+9, and Ctrl+Option+0 for session 10, from any panel.
/// Option is the Alt modifier.
/// Named AppKeys because KeyBindings is Terminal.Gui's type.
/// </summary>
internal static class AppKeys
{
    internal const string SessionsText = "F1";

    internal const string PreviewText = "F3";

    internal const string RenameText = "F6";

    internal const string CreateFolderText = "F7";

    internal const string DeleteText = "F8";

    private static readonly Key[] SessionChords =
    [
        Key.D1.WithCtrl.WithAlt,
        Key.D2.WithCtrl.WithAlt,
        Key.D3.WithCtrl.WithAlt,
        Key.D4.WithCtrl.WithAlt,
        Key.D5.WithCtrl.WithAlt,
        Key.D6.WithCtrl.WithAlt,
        Key.D7.WithCtrl.WithAlt,
        Key.D8.WithCtrl.WithAlt,
        Key.D9.WithCtrl.WithAlt,
        Key.D0.WithCtrl.WithAlt,
    ];

    internal const string QuitText = "F10";

    internal const string CenterText = "F12";

    internal const string CopyText = "Ctrl+Shift+C";

    internal const string PasteText = "Ctrl+Shift+V";

    /// <summary>
    /// Ctrl+Option+1 through Ctrl+Option+9 select sessions 1 through 9.
    /// Ctrl+Option+0 selects session 10. The index is zero-based.
    /// </summary>
    internal static bool TrySession(Key key, out int index)
    {
        for (int i = 0; i < SessionChords.Length; i++)
        {
            if (key == SessionChords[i])
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    internal static bool TryRoute(Key key, out AppCommand command)
    {
        command = default;
        if (key.IsAlt)
        {
            return false;
        }

        if (!key.IsCtrl && !key.IsShift)
        {
            if (key == Key.F1)
            {
                command = AppCommand.Sessions;
                return true;
            }

            if (key == Key.F3)
            {
                command = AppCommand.Preview;
                return true;
            }

            if (key == Key.F6)
            {
                command = AppCommand.Rename;
                return true;
            }

            if (key == Key.F7)
            {
                command = AppCommand.CreateFolder;
                return true;
            }

            if (key == Key.F8)
            {
                command = AppCommand.Delete;
                return true;
            }

            if (key == Key.F12)
            {
                command = AppCommand.Center;
                return true;
            }

            // Cmd+K is Super+K once CommandChord has put the kitty super bit back on the key.
            if (IsCommandChord(key) && key.EventType == KeyEventType.Press)
            {
                command = AppCommand.Menu;
                return true;
            }
        }

        // F9 is the Norton Commander menu key, and Terminal.app delivers it.
        if (key == Key.F9 && !key.IsCtrl && !key.IsShift)
        {
            command = AppCommand.Menu;
            return true;
        }

        // F10 quits, as in Norton Commander, without forwarding it to the child terminal.
        if (key == Key.F10 && !key.IsCtrl && !key.IsShift)
        {
            command = AppCommand.Quit;
            return true;
        }

        if (key == Key.C.WithCtrl.WithShift)
        {
            command = AppCommand.Copy;
            return true;
        }

        if (key == Key.V.WithCtrl.WithShift)
        {
            command = AppCommand.Paste;
            return true;
        }

        return false;
    }

    /// <summary>Cmd+K, including repeats and releases. Terminal.Gui stores Command as <see cref="ModifierKey.Super"/>.</summary>
    internal static bool IsCommandChord(Key key) =>
        !key.IsAlt && !key.IsCtrl && !key.IsShift
        && key == Key.K
        && key.ModifierKey is ModifierKey.Super or ModifierKey.LeftSuper or ModifierKey.RightSuper;
}
