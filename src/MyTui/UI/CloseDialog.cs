using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class CloseDialog
{
    internal static bool Confirm(IApplication app, string name)
    {
        var dialog = new Dialog
        {
            Title = "Close session",
            Width = 58,
            Height = 7,
        };
        dialog.Add(new Label
        {
            Text = Fit(name) + " is still running. Close it?",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
            CanFocus = false,
        });
        dialog.AddButton(new Button { Text = "Cancel" });
        var close = new Button { Text = "Close" };
        dialog.AddButton(close);
        CancelOnEsc(dialog, app);
        dialog.Initialized += (_, _) => close.SetFocus();
        try
        {
            app.Run(dialog);
            return dialog.Result == 1;
        }
        finally
        {
            dialog.Dispose();
        }
    }

    internal static void CancelOnEsc(Dialog dialog, IApplication app)
    {
        dialog.KeyDown += (_, key) =>
        {
            if (key != Key.Esc)
            {
                return;
            }

            app.RequestStop(dialog);
            key.Handled = true;
        };
    }

    private static string Fit(string name)
    {
        name = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return name.Length <= 40 ? name : name[..40];
    }
}
