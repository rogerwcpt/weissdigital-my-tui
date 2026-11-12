using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class FolderDialog
{
    internal static OpenDialog Create(string? start)
    {
        var dialog = new OpenDialog
        {
            Title = "Open Folder",
            OpenMode = OpenMode.Directory,
            AllowsMultipleSelection = false,
            MustExist = true,
        };
        if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
        {
            dialog.Path = start;
        }

        var table = dialog.SubViews.SelectMany(view => view.SubViews).OfType<TableView>().Single();
        table.KeyDown += (_, key) => Navigate(table, key);
        return dialog;
    }

    private static void Navigate(TableView table, Key key)
    {
        if (key != Key.CursorLeft && key != Key.CursorRight)
        {
            return;
        }

        key.Handled = true;
        if (table.Table is not { Rows: > 0 } source || table.Value is null)
        {
            return;
        }

        if (key == Key.CursorLeft)
        {
            // Use the dialog's parent entry, not Path (which can name the selected child).
            int parent = Enumerable.Range(0, source.Rows)
                .FirstOrDefault(row => source[row, 0]?.ToString() == "..", -1);
            if (parent < 0)
            {
                return;
            }

            table.SetSelection(0, parent, false);
        }

        // This dialog lists directories only; Accept navigates without closing it.
        table.InvokeCommand(Command.Accept);
    }

    internal static string? Ask(IApplication app, string? start)
    {
        var dialog = Create(start);
        dialog.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                app.RequestStop(dialog);
                key.Handled = true;
            }
        };
        try
        {
            app.Run(dialog);
            if (dialog.Canceled || string.IsNullOrEmpty(dialog.Path) || !Directory.Exists(dialog.Path))
            {
                return null;
            }

            return Path.GetFullPath(dialog.Path);
        }
        finally
        {
            dialog.Dispose();
        }
    }
}
