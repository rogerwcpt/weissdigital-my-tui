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
    public void RowsPutAHeadingBeforeEachGroup()
    {
        List<CommandPalette.Entry?> rows = CommandPalette.Rows(CommandPalette.Matches("pa", filesActive: false));
        Assert.Null(rows[0]);
        Assert.Equal("next", rows[1]!.Value.Id);
        Assert.Equal("previous", rows[2]!.Value.Id);
        Assert.Null(rows[3]);
        Assert.Equal("paste", rows[4]!.Value.Id);
        Assert.Equal(5, rows.Count);
    }
}
