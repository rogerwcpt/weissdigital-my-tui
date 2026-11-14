using MyTui.UI;

namespace MyTui.Tests;

public class SpinnerTests
{
    [Fact]
    public void QuietTextStaysPlain()
    {
        Assert.Equal("notes", Spinner.Mark("notes", busy: false, now: 0));
        Assert.Equal("notes", Spinner.Mark("notes", busy: false, now: 700));
    }

    [Fact]
    public void BusyTextPulsesUpAndDown()
    {
        Assert.Equal("notes ▁", Spinner.Mark("notes", busy: true, now: 0));
        Assert.Equal("notes ▄", Spinner.Mark("notes", busy: true, now: 700));
        Assert.Equal("notes █", Spinner.Mark("notes", busy: true, now: 1400));
        Assert.Equal("notes ▄", Spinner.Mark("notes", busy: true, now: 2100));
        Assert.Equal("notes ▁", Spinner.Mark("notes", busy: true, now: 2800));
    }

    [Fact]
    public void GlyphHoldsForTheWholeFrame()
    {
        Assert.Equal('▁', Spinner.Glyph(699));
        Assert.Equal('▄', Spinner.Glyph(700));
        Assert.Equal('▄', Spinner.Glyph(1399));
        Assert.Equal('█', Spinner.Glyph(1400));
        Assert.Equal('█', Spinner.Glyph(2099));
        Assert.Equal('▄', Spinner.Glyph(2100));
        Assert.Equal('▄', Spinner.Glyph(2799));
        Assert.Equal('▁', Spinner.Glyph(2800));
    }

    [Fact]
    public void NegativeTimeUsesTheUnderline()
    {
        Assert.Equal('▁', Spinner.Glyph(-1));
    }

    [Fact]
    public void AllFramesKeepTheSameLabelLength()
    {
        int length = Spinner.Mark("notes", busy: true, now: 0).Length;
        Assert.Equal(length, Spinner.Mark("notes", busy: true, now: 700).Length);
        Assert.Equal(length, Spinner.Mark("notes", busy: true, now: 1400).Length);
        Assert.Equal(length, Spinner.Mark("notes", busy: true, now: 2100).Length);
    }

    [Fact]
    public void SearchStillMatchesTheName()
    {
        string row = SessionScreen.Numbered(Spinner.Mark("notes", busy: true, now: 0), 0);

        Assert.StartsWith("notes", SessionScreen.SearchText(row));
    }
}
