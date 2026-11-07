using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// Theme list from the command palette. Moving the highlight paints that theme on the dialog
/// and the screen behind it. Enter, Apply, or a double-click keeps the highlight.
/// Esc or Cancel restores the theme that was open when the list appeared.
/// </summary>
internal static class ThemeDialog
{
    internal static string? Ask(IApplication app, string current, Action<string> preview)
    {
        string[] names = ThemeNames.All;
        var dialog = new Dialog
        {
            Title = "Theme",
            Width = 36,
            Height = names.Length + 6,
        };
        var list = new ThemeList
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            CanFocus = true,
        };
        list.SetSource(new ObservableCollection<string>(names));
        int selected = Array.IndexOf(names, ThemeNames.Canonical(current));
        list.SelectedItem = selected < 0 ? 0 : selected;
        Scheme scheme = HostTheme.Scheme();
        dialog.SetScheme(scheme);
        list.SetScheme(scheme);
        dialog.Add(list);
        dialog.AddButton(new Button { Text = "Cancel", HotKey = Key.Empty });
        var apply = new Button { Text = "Apply", IsDefault = true, HotKey = Key.Empty };
        dialog.AddButton(apply);
        list.ValueChanged += (_, args) =>
        {
            if (args.NewValue is not int index || index < 0 || index >= names.Length)
            {
                return;
            }

            preview(names[index]);
            Scheme highlighted = HostTheme.Scheme();
            SessionScreen.ApplyScheme(dialog, highlighted);
            foreach (Button button in dialog.Buttons)
            {
                button.SetScheme(highlighted);
            }

            dialog.SetNeedsDraw();
        };
        list.Accept = () =>
        {
            dialog.Result = 1;
            app.RequestStop(dialog);
        };
        CloseDialog.CancelOnEsc(dialog, app);
        dialog.Initialized += (_, _) => list.SetFocus();
        try
        {
            app.Run(dialog);
            if (dialog.Canceled || dialog.Result != 1)
            {
                return null;
            }

            int index = list.SelectedItem ?? -1;
            return index >= 0 && index < names.Length ? names[index] : null;
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private sealed class ThemeList : ListView
    {
        internal Action? Accept { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Enter)
            {
                Accept?.Invoke();
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            bool handled = base.OnMouseEvent(mouse);
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
            {
                Accept?.Invoke();
                return true;
            }

            return handled;
        }
    }
}
