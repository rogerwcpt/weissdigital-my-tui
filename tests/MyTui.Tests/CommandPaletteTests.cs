using MyTui.UI;

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
        Assert.Equal("open-file", Assert.Single(CommandPalette.Matches("SELECTED FILE", filesActive: true)).Id);
        Assert.Equal("copy", Assert.Single(CommandPalette.Matches("ctrl+shift+c", filesActive: false)).Id);
        Assert.Contains(CommandPalette.Matches("open", filesActive: false), entry => entry.Id == "open");
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

        Assert.All(entries.Where(entry => entry.Group != "Clipboard"), entry => Assert.Empty(entry.Detail));
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
