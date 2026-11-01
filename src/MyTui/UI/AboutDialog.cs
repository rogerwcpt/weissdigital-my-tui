using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class AboutDialog
{
    internal static void Show(IApplication app)
    {
        var dialog = new Dialog
        {
            Title = "About",
            Width = 52,
            Height = 13,
        };
        dialog.Add(Line(0, "  ╭──┬──────┬──╮"));
        dialog.Add(Line(1, "  │  │  ▮   │  │"));
        dialog.Add(Line(2, "  ╰──┴──────┴──╯"));
        dialog.Add(Line(4, "my-tui"));
        dialog.Add(Line(6, "A terminal-first session orchestrator."));
        dialog.Add(Line(8, "Weiss Digital"));
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

    private static Label Line(int y, string text) => new()
    {
        Text = text,
        X = 0,
        Y = y,
        Width = Dim.Fill(),
        Height = 1,
        CanFocus = false,
    };
}
