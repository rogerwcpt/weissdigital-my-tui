using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace MyTui.UI;

internal static class FolderDialog
{
    internal static string? Ask(IApplication app, string? start)
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
