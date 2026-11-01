using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class HelpDialog
{
    internal static void Show(IApplication app)
    {
        IReadOnlyList<string> lines = AppKeys.HelpLines();
        var dialog = new Dialog
        {
            Title = "Keystrokes",
            Width = 72,
            Height = lines.Count + 6,
        };
        for (int i = 0; i < lines.Count; i++)
        {
            dialog.Add(new Label
            {
                Text = lines[i],
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
