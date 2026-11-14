using MyTui.UI;

namespace MyTui.Tests;

public class SpinnerTests
{
    [Fact]
    public void QuietTextStaysPlain()
    {
        Assert.Equal("notes", Spinner.Mark("notes", busy: false, now: 0));
        Assert.Equal("notes", Spinner.Mark("notes", busy: false, now: 480));
    }

    [Fact]
    public void BusyTextTurnsClockwise()
    {
        Assert.Equal("notes |", Spinner.Mark("notes", busy: true, now: 0));
        Assert.Equal("notes /", Spinner.Mark("notes", busy: true, now: 120));
        Assert.Equal("notes -", Spinner.Mark("notes", busy: true, now: 240));
        Assert.Equal("notes \\", Spinner.Mark("notes", busy: true, now: 360));
        Assert.Equal("notes |", Spinner.Mark("notes", busy: true, now: 480));
    }

    [Fact]
    public void GlyphHoldsForTheWholeFrame()
    {
        Assert.Equal(Spinner.Glyph(120), Spinner.Glyph(239));
        Assert.NotEqual(Spinner.Glyph(119), Spinner.Glyph(120));
    }

    [Fact]
    public void SearchStillMatchesTheName()
    {
        string row = SessionScreen.Numbered(Spinner.Mark("notes", busy: true, now: 0), 0);

        Assert.StartsWith("notes", SessionScreen.SearchText(row));
    }
}
