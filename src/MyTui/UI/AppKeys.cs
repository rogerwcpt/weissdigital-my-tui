using Terminal.Gui.Input;

namespace MyTui.UI;

internal enum AppCommand
{
    Menu,
    Preview,
    Rename,
    Delete,
    Quit,
    Copy,
    Paste,
}

/// <summary>
/// The few keys my-tui keeps for itself, even inside a live terminal. Host terminals and agent
/// CLIs swallow most chords, so most commands are in the F9 palette only. Rename and delete
/// are reserved only while the Sessions or Files panel has focus.
/// Named AppKeys because KeyBindings is Terminal.Gui's type.
/// </summary>
internal static class AppKeys
{
    internal const string PreviewText = "F3";

    internal const string RenameText = "F6";

    internal const string DeleteText = "F8";

    internal const string QuitText = "F10";

    internal const string CopyText = "Ctrl+Shift+C";

    internal const string PasteText = "Ctrl+Shift+V";

    internal static bool TryRoute(Key key, out AppCommand command)
    {
        command = default;
        if (key.IsAlt)
        {
            return false;
        }

        if (!key.IsCtrl && !key.IsShift)
        {
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

            if (key == Key.F8)
            {
                command = AppCommand.Delete;
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
}
