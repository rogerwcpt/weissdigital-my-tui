using Terminal.Gui.Input;

namespace MyTui.UI;

internal enum AppCommand
{
    Menu,
    Copy,
    Paste,
}

/// <summary>
/// The few keys my-tui keeps for itself, even inside a live terminal. Host terminals and agent
/// CLIs swallow most chords, so every other command is in the F9 palette only.
/// Named AppKeys because KeyBindings is Terminal.Gui's type.
/// </summary>
internal static class AppKeys
{
    internal const string CopyText = "Ctrl+Shift+C";

    internal const string PasteText = "Ctrl+Shift+V";

    internal static bool TryRoute(Key key, out AppCommand command)
    {
        command = default;
        if (key.IsAlt)
        {
            return false;
        }

        // F9 is the Norton Commander menu key, and Terminal.app delivers it.
        if (key == Key.F9 && !key.IsCtrl && !key.IsShift)
        {
            command = AppCommand.Menu;
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
