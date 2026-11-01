using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class HelpDialog
{
    private static readonly string[] Lines =
    [
        "F1            menu",
        "Ctrl+N        new session",
        "+             new session",
        "Ctrl+O        list, tab, or preview",
        "Ctrl+Tab      Sessions, Terminal, or Files",
        "Ctrl+Shift+Tab  previous panel",
        "F4            hide or show Sessions",
        "F11           maximize or restore",
        "□ −           maximize or hide that panel",
        "Ctrl+T        Terminal or Files",
        "Ctrl+P        open or close the preview",
        "Ctrl+E        edit the session",
        "Delete        close (asks when it is running)",
        "×             close the active session",
        "Enter         start, or focus the pane",
        "Ctrl+R        refresh Files",
        "Alt+Up/Down   reorder the list",
        "F2            templates",
        "Shift+PgUp    scroll up",
        "Shift+PgDn    scroll down",
        "Shift+Home    top of the history",
        "Shift+End     back to the prompt",
        "Ctrl+Shift+C  copy visible text",
        "Ctrl+Shift+V  paste",
        "Keystrokes    this list, from the menu",
        "Ctrl+Q        quit",
    ];

    internal static void Show(IApplication app)
    {
        var dialog = new Dialog
        {
            Title = "Keystrokes",
            Width = 56,
            Height = Lines.Length + 6,
        };
        for (int i = 0; i < Lines.Length; i++)
        {
            dialog.Add(new Label
            {
                Text = Lines[i],
                X = 0,
                Y = i,
                Width = Dim.Fill(),
                Height = 1,
                CanFocus = false,
            });
        }

        dialog.AddButton(new Button { Text = "Close" });
        CloseDialog.CancelOnEsc(dialog, app);
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.Dispose();
        }
    }
}
