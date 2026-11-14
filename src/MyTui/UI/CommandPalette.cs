using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// One list of commands, in groups. F9 opens it from anywhere, including a live terminal.
/// </summary>
internal static class CommandPalette
{
    internal const string Shortcut = "F9";

    internal const string Footer = "Command Palette F9";

    internal readonly record struct Entry(string Group, string Title, string Detail, string Id);

    private static readonly string[] Groups = ["Session", "Layout", "Files", "Clipboard", "App"];

    private static readonly Entry[] Entries = Sort(
    [
        new("Session", "New session", "", "new"),
        new("Session", "Duplicate session", "", "duplicate"),
        new("Session", "Start the session", "", "start"),
        new("Session", "Open folder", "", "open"),
        new("Session", "Edit the session", "", "edit"),
        new("Session", "Rename the session", AppKeys.RenameText, "rename"),
        new("Session", "Sort Sessions by Name", "", "sort"),
        new("Session", "Force stop terminal", "", "stop"),
        new("Session", "Templates", "", "templates"),
        new("Session", "Close the session", AppKeys.DeleteText, "close"),
        new("Layout", "Toggle Sessions Panel", AppKeys.SessionsText, "hide"),
        new("Layout", "Maximize/Restore", "", "maximize"),
        new("Layout", "Toggle Preview Panel", AppKeys.PreviewText, "preview"),
        new("Layout", "Toggle Center Panel", AppKeys.CenterText, "center"),
        new("Layout", "Restore layout", "", "restore"),
        new("Layout", "Show Terminal", "", "terminal"),
        new("Layout", "Show Files", "", "files"),
        new("Files", "Open selected file", "", "open-file"),
        new("Files", "Create folder", AppKeys.CreateFolderText, "create-folder"),
        new("Files", "Rename selected file or folder", AppKeys.RenameText, "rename-file"),
        new("Files", "Delete selected file or folder", AppKeys.DeleteText, "delete-file"),
        new("Files", OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Reveal in File Manager", "", "reveal"),
        new("Files", "Refresh Files", "", "refresh"),
        new("Files", "Run selected script", "", "run-script"),
        new("Files", "Show script output", "", "script-output"),
        new("Files", "Stop script", "", "stop-script"),
        new("Clipboard", "Copy selection or screen", AppKeys.CopyText, "copy"),
        new("Clipboard", "Paste", AppKeys.PasteText, "paste"),
        new("App", "Theme", "", "theme"),
        new("App", "About", "", "about"),
        new("App", "Quit", AppKeys.QuitText, "quit"),
    ]);

    private static readonly int NameColumn = EntriesMax(static entry => entry.Title.Length);

    private static readonly int ShortcutColumn = EntriesMax(static entry => entry.Detail.Length);

    internal const string GotoPrefix = "goto:";

    /// <summary>
    /// Sessions first, in list order, then the fixed commands. A number in the title matches the
    /// session list. The first ten rows also show the Ctrl+Option chord.
    /// </summary>
    internal static IEnumerable<Entry> Matches(string text, bool filesActive, IReadOnlyList<string>? sessions = null)
    {
        if (sessions is not null)
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                Entry entry = SessionEntry(sessions[i], i);
                if (Hits(entry, text))
                {
                    yield return entry;
                }
            }
        }

        foreach (Entry entry in Entries)
        {
            if ((filesActive || entry.Id is not ("open-file" or "run-script" or "rename-file" or "delete-file" or "create-folder"))
                && Hits(entry, text))
            {
                yield return entry;
            }
        }
    }

    internal static Entry SessionEntry(string name, int index) =>
        new("Go to", SessionScreen.Numbered(name, index), SessionShortcut(index), GotoId(index));

    internal static string GotoId(int index) =>
        GotoPrefix + index.ToString(CultureInfo.InvariantCulture);

    internal static bool TryGoto(string? id, out int index)
    {
        index = -1;
        if (id is null || !id.StartsWith(GotoPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(id.AsSpan(GotoPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index >= 0;
    }

    /// <summary>1 through 9 select sessions 1 through 9. 0 selects session 10. The index is zero-based.</summary>
    internal static bool TryBareDigit(Key key, out int index)
    {
        index = -1;
        if (key.IsCtrl || key.IsAlt || key.IsShift)
        {
            return false;
        }

        int digit = (int)key.KeyCode - (int)Key.D0.KeyCode;
        if (digit is < 0 or > 9)
        {
            return false;
        }

        index = digit == 0 ? 9 : digit - 1;
        return true;
    }

    internal static string SessionShortcut(int index) => index switch
    {
        < 9 => "Ctrl+Option+" + (index + 1).ToString(CultureInfo.InvariantCulture),
        9 => "Ctrl+Option+0",
        _ => "",
    };

    private static bool Hits(Entry entry, string text) =>
        text.Length == 0
        || entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
        || entry.Detail.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rows as shown: a null entry is a group heading, followed by that group's matches.</summary>
    internal static List<Entry?> Rows(IEnumerable<Entry> matches)
    {
        var rows = new List<Entry?>();
        string? group = null;
        foreach (Entry entry in matches)
        {
            if (entry.Group != group)
            {
                group = entry.Group;
                rows.Add(null);
            }

            rows.Add(entry);
        }

        return rows;
    }

    private static Entry[] Sort(Entry[] entries) =>
        entries
            .OrderBy(entry => Array.IndexOf(Groups, entry.Group))
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string? Ask(IApplication app, bool filesActive, IReadOnlyList<string>? sessions = null)
    {
        var shown = new ObservableCollection<string>();
        List<Entry?> rows = [];
        var filling = false;
        var last = 1;
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
            List<Entry> found = Matches(text, filesActive, sessions).ToList();
            rows = Rows(found);
            int width = Math.Max(list.Viewport.Width, NameColumn + ShortcutColumn + 2);
            shown.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] is not Entry entry)
                {
                    string group = rows[i + 1]!.Value.Group;
                    shown.Add("\u2500\u2500 " + group + " " + new string('\u2500', Math.Max(0, width - group.Length - 4)));
                    continue;
                }

                int gap = width - entry.Title.Length - entry.Detail.Length;
                if (gap < 2)
                {
                    gap = 2;
                }

                shown.Add(entry.Title + new string(' ', gap) + entry.Detail);
            }

            bool keep = list.SelectedItem is int selected && selected >= 0 && selected < rows.Count && rows[selected] is not null;
            if (found.Count > 0 && !keep)
            {
                list.SelectedItem = 1;
            }

            last = list.SelectedItem ?? 1;
            filling = false;
        }

        string? chosen = null;
        void Accept()
        {
            int index = list.SelectedItem is int selected ? selected : 0;
            if (index < 0 || index >= rows.Count || rows[index] is not Entry entry)
            {
                return;
            }

            chosen = entry.Id;
            app.RequestStop(dialog);
        }

        // A bare number jumps to that session while the filter is empty. The Ctrl+Option chord
        // does the same even after typing, because it is not a search character.
        bool TakeDigit(Key key)
        {
            bool chord = AppKeys.TrySession(key, out int index);
            bool bare = !chord && TryBareDigit(key, out index);
            if (!chord && !bare)
            {
                return false;
            }

            if (chosen is not null)
            {
                return true;
            }

            if (bare && (query.Text?.ToString()?.Length ?? 0) > 0)
            {
                return false;
            }

            if (sessions is null || index < 0 || index >= sessions.Count)
            {
                return false;
            }

            chosen = GotoId(index);
            app.RequestStop(dialog);
            return true;
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
            if (TakeDigit(key))
            {
                key.Handled = true;
                return;
            }

            if (key == Key.Enter)
            {
                Accept();
                key.Handled = true;
                return;
            }

            if (key == Key.CursorDown && rows.Count > 0)
            {
                list.SetFocus();
                key.Handled = true;
            }
        };
        list.Activate = Accept;
        list.TakeKey = TakeDigit;
        list.IsHeading = row => row >= 0 && row < rows.Count && rows[row] is null;
        // Headings are not commands. Selection steps over them in the direction it was moving.
        list.ValueChanged += (_, _) =>
        {
            if (filling || list.SelectedItem is not int row)
            {
                return;
            }

            if (row >= 0 && row < rows.Count && rows[row] is null)
            {
                int step = row < last ? -1 : 1;
                int next = row + step;
                if (next < 0 || next >= rows.Count)
                {
                    next = row - step;
                }

                filling = true;
                list.SelectedItem = next;
                filling = false;
                row = next;
            }

            last = row;
        };
        dialog.KeyDown += (_, key) =>
        {
            if (TakeDigit(key))
            {
                key.Handled = true;
                return;
            }

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

    /// <summary>Enter and a click both run the highlighted command. The list would otherwise swallow them. Headings ignore clicks.</summary>
    private sealed class PaletteList : ListView
    {
        internal Action? Activate { get; set; }

        internal Func<Key, bool>? TakeKey { get; set; }

        internal Func<int, bool>? IsHeading { get; set; }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Enter)
            {
                Activate?.Invoke();
                return true;
            }

            if (TakeKey?.Invoke(key) == true)
            {
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Position is { } position && IsHeading?.Invoke(Viewport.Y + (int)position.Y) == true)
            {
                return true;
            }

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
