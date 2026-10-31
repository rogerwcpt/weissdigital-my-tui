using MyTui.Sessions;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal readonly record struct SessionDraft(string Name, string Cwd, string Command, string Args);

internal static class SessionDialog
{
    internal static SessionDraft? Ask(
        IApplication app,
        string title,
        SessionDraft initial,
        string accept,
        bool mustResolve)
    {
        var dialog = new Dialog
        {
            Title = title,
            Width = 68,
            Height = 14,
        };
        var name = Field(0, "Name", initial.Name);
        var cwd = Field(2, "Cwd", initial.Cwd);
        var command = Field(4, "Command", initial.Command);
        var args = Field(6, "Args", initial.Args);
        var error = new Label
        {
            X = 0,
            Y = 8,
            Width = Dim.Fill(),
            Height = 2,
            CanFocus = false,
            Text = "",
        };
        dialog.Add(name.Label, name.Input, cwd.Label, cwd.Input, command.Label, command.Input, args.Label, args.Input, error);

        string? Problem()
        {
            if (Read(name.Input).Length == 0)
            {
                return "Name is empty.";
            }

            if (Read(command.Input).Length == 0)
            {
                return "Command is empty.";
            }

            if (!Directory.Exists(Read(cwd.Input)))
            {
                return "cwd is not a directory.";
            }

            if (mustResolve && CommandLine.Resolve(Read(command.Input)) is null)
            {
                return "Command was not found.";
            }

            return null;
        }

        dialog.Accepting += (_, argsEvent) =>
        {
            string? problem = Problem();
            if (problem is null)
            {
                return;
            }

            error.Text = problem;
            argsEvent.Handled = true;
        };

        dialog.AddButton(new Button { Text = "Cancel" });
        dialog.AddButton(new Button { Text = accept });
        dialog.Initialized += (_, _) =>
        {
            TextField focused = accept == "Save" ? name.Input : command.Input;
            focused.SetFocus();
            focused.SelectAll();
        };

        try
        {
            app.Run(dialog);
            if (dialog.Result != 1)
            {
                return null;
            }

            return new SessionDraft(Read(name.Input), Read(cwd.Input), Read(command.Input), Read(args.Input));
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private static string Read(TextField field) => field.Text?.ToString()?.Trim() ?? "";

    private static (Label Label, TextField Input) Field(int y, string caption, string value)
    {
        var label = new Label
        {
            Text = caption,
            X = 0,
            Y = y,
            Width = 10,
            Height = 1,
            CanFocus = false,
        };
        var input = new TextField
        {
            Text = value,
            X = 11,
            Y = y,
            Width = Dim.Fill(),
            Height = 1,
        };
        return (label, input);
    }
}
