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
        Assert.Equal("new", Assert.Single(CommandPalette.Matches("ctrl+n", filesActive: false)).Id);
        Assert.Contains(CommandPalette.Matches("open", filesActive: false), entry => entry.Id == "open");
    }
}
