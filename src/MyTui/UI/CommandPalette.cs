using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// One list of commands. Ctrl+Shift+P opens it from anywhere, including a live terminal.
/// Cmd+Shift+P stays with the host and never arrives here.
/// </summary>
internal static class CommandPalette
{
    internal const string Shortcut = "F9";

    internal const string Footer = "Palette  F9";

    internal readonly record struct Entry(string Title, string Detail, string Id);

    private static readonly Entry[] Entries =
    [
        new("New session", "Ctrl+N", "new"),
        new("Start the session", "Enter", "start"),
        new("Open folder", "", "open"),
        new("Open selected file", "", "open-file"),
        new("List, tab, or preview", "Ctrl+O", "focus"),
        new("Next panel", "Ctrl+Tab", "next"),
        new("Previous panel", "Ctrl+Shift+Tab", "previous"),
        new("Hide or show Sessions", "F4", "hide"),
        new("Maximize or restore", "F11", "maximize"),
        new("Terminal or Files", "Ctrl+T", "tab"),
        new("Open or close the preview", "Ctrl+P", "preview"),
        new("Edit the session", "Ctrl+E", "edit"),
        new("Rename the session", "F6", "rename"),
        new("Refresh Files", "Ctrl+R", "refresh"),
        new("Templates", "F2", "templates"),
        new("Close the session", "Delete", "close"),
        new("Copy selection or screen", "Ctrl+Shift+C", "copy"),
        new("Paste", "Ctrl+Shift+V", "paste"),
        new("Theme", "", "theme"),
        new("About", "", "about"),
        new("Quit", "Ctrl+Q", "quit"),
    ];

    private static readonly int NameColumn = EntriesMax(static entry => entry.Title.Length);

    private static readonly int ShortcutColumn = EntriesMax(static entry => entry.Detail.Length);

    internal static IEnumerable<Entry> Matches(string text, bool filesActive) =>
        Entries.Where(entry =>
            (filesActive || entry.Id != "open-file")
            && (text.Length == 0
                || entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Detail.Contains(text, StringComparison.OrdinalIgnoreCase)));

    internal static string? Ask(IApplication app, bool filesActive)
    {
        var shown = new ObservableCollection<string>();
        List<Entry> matches = [];
        var filling = false;
        var dialog = new Dialog
        {
            Title = "Commands",
            Width = 64,
            Height = 20,
        };
        var query = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = 1,
        };
        var list = new PaletteList
        {
            X = 1,
            Y = 3,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1),
            CanFocus = true,
        };
        list.SetSource(shown);
        Scheme scheme = HostTheme.Scheme();
        dialog.SetScheme(scheme);
        query.SetScheme(scheme);
        list.SetScheme(scheme);

        void Refilter()
        {
            if (filling)
            {
                return;
            }

            filling = true;
            string text = query.Text?.ToString()?.Trim() ?? "";
            matches = Matches(text, filesActive).ToList();
            int width = Math.Max(list.Viewport.Width, NameColumn + ShortcutColumn + 2);
            shown.Clear();
            foreach (Entry entry in matches)
            {
                int gap = width - entry.Title.Length - entry.Detail.Length;
                if (gap < 2)
                {
                    gap = 2;
                }

                shown.Add(entry.Title + new string(' ', gap) + entry.Detail);
            }

            bool keep = list.SelectedItem is int selected && selected >= 0 && selected < matches.Count;
            if (matches.Count > 0 && !keep)
            {
                list.SelectedItem = 0;
            }

            filling = false;
        }

        string? chosen = null;
        void Accept()
        {
            int index = list.SelectedItem is int selected ? selected : 0;
            if (index < 0 || index >= matches.Count)
            {
                return;
            }

            chosen = matches[index].Id;
            app.RequestStop(dialog);
        }

        var laidOutWidth = 0;
        query.TextChanged += (_, _) => Refilter();
        // Width changes the right-aligned shortcut column. Scroll changes Viewport.Y, and
        // rebuilding the rows then would jump the keyboard back to the top of the list.
        list.ViewportChanged += (_, _) =>
        {
            if (list.Viewport.Width == laidOutWidth)
            {
                return;
            }

            laidOutWidth = list.Viewport.Width;
            Refilter();
        };
        query.KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                Accept();
                key.Handled = true;
                return;
            }

            if (key == Key.CursorDown && matches.Count > 0)
            {
                list.SetFocus();
                key.Handled = true;
            }
        };
        list.Activate = Accept;
        dialog.KeyDown += (_, key) =>
        {
            if (key != Key.Enter)
            {
                return;
            }

            Accept();
            key.Handled = true;
        };
        dialog.Add(query, list);
        CloseDialog.CancelOnEsc(dialog, app);
        Refilter();
        dialog.Initialized += (_, _) => query.SetFocus();
        try
        {
            app.Run(dialog);
            return chosen;
        }
        finally
        {
            dialog.Dispose();
        }
    }

    private static int EntriesMax(Func<Entry, int> measure)
    {
        int width = 0;
        foreach (Entry entry in Entries)
        {
            width = Math.Max(width, measure(entry));
        }

        return width;
    }

    /// <summary>Enter and a click both run the highlighted command. The list would otherwise swallow them.</summary>
    private sealed class PaletteList : ListView
    {
        internal Action? Activate { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Enter)
            {
                Activate?.Invoke();
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            bool handled = base.OnMouseEvent(mouse);
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                Activate?.Invoke();
                return true;
            }

            return handled;
        }
    }
}

/// <summary>One row of line, so the palette footer sits in its own box.</summary>
internal sealed class PaletteRule : View
{
    internal PaletteRule()
    {
        CanFocus = false;
        X = 0;
        Y = Pos.AnchorEnd(2);
        Width = Dim.Fill();
        Height = 1;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        int width = Math.Max(0, Viewport.Width);
        for (int column = 0; column < width; column++)
        {
            AddRune(column, 0, new Rune('\u2500'));
        }

        return true;
    }
}
