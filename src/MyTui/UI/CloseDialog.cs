using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class CloseDialog
{
    internal static bool Confirm(IApplication app, string name)
        => Confirm(app, name, force: false);

    internal static bool ConfirmStop(IApplication app, string name)
        => Confirm(app, name, force: true);

    private static bool Confirm(IApplication app, string name, bool force)
    {
        var dialog = new Dialog
        {
            Title = force ? "Force stop terminal" : "Close session",
            Width = 58,
            Height = 7,
        };
        dialog.Add(new Label
        {
            Text = force
                ? Fit(name) + ": force stop? Unsaved work may be lost."
                : Fit(name) + " is still running. Close it?",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
            CanFocus = false,
        });
        var cancel = new Button { Text = "Cancel" };
        dialog.AddButton(cancel);
        var close = new Button { Text = force ? "Force stop" : "Close" };
        dialog.AddButton(close);
        if (force)
        {
            close.IsDefault = false;
            cancel.IsDefault = true;
        }

        CancelOnEsc(dialog, app);
        dialog.Initialized += (_, _) => (force ? cancel : close).SetFocus();
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

    internal static bool ConfirmQuit(IApplication app, int running)
    {
        string message = running == 1
            ? "1 session is still running. Quit and stop it?"
            : running + " sessions are still running. Quit and stop them?";
        var dialog = new Dialog
        {
            Title = "Quit",
            Width = 62,
            Height = 7,
        };
        dialog.Add(new Label
        {
            Text = message,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
            CanFocus = false,
        });
        var cancel = new Button { Text = "Cancel" };
        var quit = new Button { Text = "Quit" };
        dialog.AddButton(cancel);
        dialog.AddButton(quit);
        // The last button added becomes the default, which Enter runs. Cancel has to be that button.
        quit.IsDefault = false;
        cancel.IsDefault = true;
        CancelOnEsc(dialog, app);
        dialog.Initialized += (_, _) => cancel.SetFocus();
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
