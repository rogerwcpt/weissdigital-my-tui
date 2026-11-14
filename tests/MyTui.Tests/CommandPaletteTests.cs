using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class CommandPaletteTests
{
    [Theory]
    [InlineData("")]
    [InlineData("open")]
    [InlineData("selected file")]
    public void OpenSelectedFileIsOnlyAvailableInFiles(string query)
    {
        Assert.Contains(CommandPalette.Matches(query, filesActive: true), entry => entry.Id == "open-file");
        Assert.DoesNotContain(CommandPalette.Matches(query, filesActive: false), entry => entry.Id == "open-file");
    }

    [Fact]
    public void RunScriptIsFilesOnlyButOutputAndStopAreAvailableFromEitherTab()
    {
        Assert.Equal("run-script", Assert.Single(CommandPalette.Matches("run selected script", filesActive: true)).Id);
        Assert.Empty(CommandPalette.Matches("run selected script", filesActive: false));
        foreach (bool filesActive in new[] { false, true })
        {
            Assert.Equal("script-output", Assert.Single(CommandPalette.Matches("show script output", filesActive)).Id);
            Assert.Equal("stop-script", Assert.Single(CommandPalette.Matches("stop script", filesActive)).Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateSessionIsAvailableInEitherTab(bool filesActive)
    {
        Assert.Contains(CommandPalette.Matches("", filesActive), entry => entry.Id == "duplicate");
        CommandPalette.Entry entry = Assert.Single(CommandPalette.Matches("DUPLICATE", filesActive));
        Assert.Equal("duplicate", entry.Id);
        Assert.Equal("Duplicate session", entry.Title);
    }

    [Theory]
    [InlineData("Toggle Preview Panel", "preview")]
    [InlineData("Toggle Sessions Panel", "hide")]
    [InlineData("Maximize/Restore", "maximize")]
    [InlineData("Toggle Center Panel", "center")]
    [InlineData("Restore layout", "restore")]
    [InlineData("Show Terminal", "terminal")]
    [InlineData("Show Files", "files")]
    [InlineData("sort sessions by name", "sort")]
    [InlineData("force stop terminal", "stop")]
    [InlineData("reveal", "reveal")]
    public void WorkspaceCommandsAreSearchableInEitherTab(string query, string id)
    {
        Assert.Equal(id, Assert.Single(CommandPalette.Matches(query, filesActive: false)).Id);
        Assert.Equal(id, Assert.Single(CommandPalette.Matches(query, filesActive: true)).Id);
    }

    [Fact]
    public void FilteringStillMatchesTitlesAndShortcuts()
    {
        Assert.Equal("open-file", Assert.Single(CommandPalette.Matches("OPEN SELECTED FILE", filesActive: true)).Id);
        Assert.Equal("copy", Assert.Single(CommandPalette.Matches("ctrl+shift+c", filesActive: false)).Id);
        Assert.Contains(CommandPalette.Matches("open", filesActive: false), entry => entry.Id == "open");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuitAdvertisesF10InEitherTab(bool filesActive)
    {
        CommandPalette.Entry entry = Assert.Single(CommandPalette.Matches("F10", filesActive));
        Assert.Equal("quit", entry.Id);
        Assert.Equal("F10", entry.Detail);
        Assert.Equal("Command Palette F9", CommandPalette.Footer);
        Assert.DoesNotContain("Quit", CommandPalette.Footer);
        Assert.DoesNotContain("F10", CommandPalette.Footer);
    }

    [Theory]
    [InlineData("create-folder", "F7")]
    [InlineData("rename-file", "F6")]
    [InlineData("delete-file", "F8")]
    public void FileMutationsAreFilesOnlyAndShowTheirKeys(string id, string shortcut)
    {
        Assert.DoesNotContain(CommandPalette.Matches("", filesActive: false), entry => entry.Id == id);
        Assert.Equal(shortcut, Assert.Single(CommandPalette.Matches("", filesActive: true), entry => entry.Id == id).Detail);
    }

    [Theory]
    [InlineData("preview", "F3")]
    [InlineData("rename", "F6")]
    [InlineData("close", "F8")]
    [InlineData("center", "F12")]
    [InlineData("hide", "F1")]
    public void ExistingActionsShowTheirCommanderKeys(string id, string shortcut)
    {
        Assert.Equal(shortcut, Assert.Single(CommandPalette.Matches("", filesActive: false), entry => entry.Id == id).Detail);
    }

    [Fact]
    public void EntriesAreGroupedAndSortedWithinEachGroup()
    {
        List<CommandPalette.Entry> entries = CommandPalette.Matches("", filesActive: true).ToList();
        List<string> groups = entries.Select(entry => entry.Group).Distinct().ToList();
        Assert.Equal(["Session", "Layout", "Files", "Clipboard", "App"], groups);
        foreach (string group in groups)
        {
            List<string> titles = entries.Where(entry => entry.Group == group).Select(entry => entry.Title).ToList();
            Assert.Equal(titles.Order(StringComparer.OrdinalIgnoreCase), titles);
        }

        Assert.All(entries.Where(entry => entry.Group != "Clipboard"
                && entry.Id is not ("quit" or "preview" or "rename" or "close" or "center" or "hide"
                    or "rename-file" or "delete-file" or "create-folder")),
            entry => Assert.Empty(entry.Detail));
    }

    [Fact]
    public void LayoutContainsOnlyTheExplicitPanelCommands()
    {
        List<string> titles = CommandPalette.Matches("", filesActive: true)
            .Where(entry => entry.Group == "Layout")
            .Select(entry => entry.Title)
            .ToList();

        Assert.Equal(
            ["Maximize/Restore", "Restore layout", "Show Files", "Show Terminal",
             "Toggle Center Panel", "Toggle Preview Panel", "Toggle Sessions Panel"],
            titles);
    }

    [Fact]
    public void SessionsLeadThePaletteInListOrder()
    {
        string[] sessions = ["zeta", "alpha"];
        List<CommandPalette.Entry> entries = CommandPalette.Matches("", filesActive: false, sessions).ToList();

        Assert.Equal("Go to", entries[0].Group);
        Assert.Equal(SessionScreen.Numbered("zeta", 0), entries[0].Title);
        Assert.Equal("Ctrl+Option+1", entries[0].Detail);
        Assert.Equal("goto:0", entries[0].Id);
        Assert.Equal(SessionScreen.Numbered("alpha", 1), entries[1].Title);
        Assert.Equal("Ctrl+Option+2", entries[1].Detail);
        Assert.Equal(
            ["Go to", "Session", "Layout", "Files", "Clipboard", "App"],
            entries.Select(entry => entry.Group).Distinct().ToList());
    }

    [Fact]
    public void EleventhSessionKeepsItsNumberWithoutAChord()
    {
        string[] sessions = Enumerable.Range(1, 11).Select(number => "s" + number).ToArray();
        List<CommandPalette.Entry> going = CommandPalette.Matches("", filesActive: false, sessions)
            .Where(entry => entry.Group == "Go to")
            .ToList();

        Assert.Equal(11, going.Count);
        Assert.Equal("Ctrl+Option+0", going[9].Detail);
        Assert.Equal("goto:9", going[9].Id);
        Assert.Equal("", going[10].Detail);
        Assert.Equal(SessionScreen.Numbered("s11", 10), going[10].Title);
    }

    [Fact]
    public void FilterMatchesASessionNameOrItsChord()
    {
        string[] sessions = ["notes", "build"];

        Assert.Equal("goto:0", Assert.Single(
            CommandPalette.Matches("NOTES", filesActive: false, sessions),
            entry => entry.Group == "Go to").Id);
        Assert.Contains(
            CommandPalette.Matches("ctrl+option+2", filesActive: false, sessions),
            entry => entry.Id == "goto:1");
        Assert.DoesNotContain(
            CommandPalette.Matches("ctrl+option+2", filesActive: false, sessions),
            entry => entry.Id == "goto:0");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(9, 8)]
    [InlineData(0, 9)]
    public void BareDigitSelectsThatSession(int digit, int index)
    {
        Key key = digit switch
        {
            0 => Key.D0,
            1 => Key.D1,
            2 => Key.D2,
            _ => Key.D9,
        };

        Assert.True(CommandPalette.TryBareDigit(key, out int found));
        Assert.Equal(index, found);
        Assert.False(CommandPalette.TryBareDigit(key.WithCtrl.WithAlt, out _));
        Assert.False(CommandPalette.TryBareDigit(key.WithShift, out _));
    }

    [Fact]
    public void GotoIdRoundTrips()
    {
        Assert.Equal("goto:10", CommandPalette.GotoId(10));
        Assert.True(CommandPalette.TryGoto("goto:10", out int index));
        Assert.Equal(10, index);
        Assert.False(CommandPalette.TryGoto("quit", out _));
        Assert.False(CommandPalette.TryGoto("goto:", out _));
        Assert.False(CommandPalette.TryGoto("goto:-1", out _));
    }

    [Fact]
    public void RowsPutAHeadingBeforeEachGroup()
    {
        List<CommandPalette.Entry?> rows = CommandPalette.Rows(CommandPalette.Matches("pa", filesActive: false));
        Assert.Null(rows[0]);
        Assert.Equal("center", rows[1]!.Value.Id);
        Assert.Equal("preview", rows[2]!.Value.Id);
        Assert.Equal("hide", rows[3]!.Value.Id);
        Assert.Null(rows[4]);
        Assert.Equal("paste", rows[5]!.Value.Id);
        Assert.Equal(6, rows.Count);
    }
}
