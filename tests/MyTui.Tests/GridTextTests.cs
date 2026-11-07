using MyTui.Terminal;

namespace MyTui.Tests;

public class GridTextTests
{
    [Fact]
    public void Copy_JoinsWrappedRowsAndKeepsHardBreaks()
    {
        string[][] rows =
        [
            Cells("hello", 8),
            Cells("world", 8),
        ];
        Assert.Equal("helloworld", GridText.Copy(rows, [true, false], 0, 0, 4, 1));
        Assert.Equal("hello\nworld", GridText.Copy(rows, [false, false], 0, 0, 4, 1));
        Assert.Equal("llo\nwor", GridText.Copy(rows, [false, false], 2, 0, 2, 1));
    }

    [Fact]
    public void Copy_TrimsTrailingSpacesAndReversesTheDrag()
    {
        string[][] rows = [Cells("ab  cd", 8)];
        Assert.Equal("ab  cd", GridText.Copy(rows, [false], 5, 0, 0, 0));
        Assert.Equal("ab", GridText.Copy(rows, [false], 0, 0, 3, 0));
    }

    [Fact]
    public void Word_CrossesAWrapAndStopsAtSpace()
    {
        string[][] rows =
        [
            Cells("https://ex", 10),
            Cells("ample.com ", 10),
        ];
        Assert.True(GridText.TryWord(rows, [true, false], 0, 1, out CellRange range));
        Assert.Equal("https://example.com", GridText.Copy(rows, [true, false], range.Column1, range.Row1, range.Column2, range.Row2));
        Assert.False(GridText.TryWord(rows, [true, false], 9, 1, out _));
    }

    [Fact]
    public void Line_CoversTheWrappedLogicalLine()
    {
        string[][] rows =
        [
            Cells("alpha", 8),
            Cells("beta", 8),
            Cells("gamma", 8),
        ];
        Assert.True(GridText.TryLine(rows, [true, false, false], 1, out CellRange range));
        Assert.Equal("alphabeta", GridText.Copy(rows, [true, false, false], range.Column1, range.Row1, range.Column2, range.Row2));
    }

    [Fact]
    public void UrlAt_TrimsPunctuationAndKeepsBalancedParentheses()
    {
        string[][] rows = [Cells("see https://example.com/a. and https://example.com/A_(b)", 80)];
        Assert.Equal("https://example.com/a", GridText.UrlAt(rows, [false], 4, 0));
        Assert.Equal("https://example.com/A_(b)", GridText.UrlAt(rows, [false], 40, 0));
        Assert.Null(GridText.UrlAt(rows, [false], 0, 0));
        Assert.False(GridText.IsOpenable("javascript:alert(1)"));
        Assert.False(GridText.IsOpenable("https://example.com/a\nb"));
        Assert.True(GridText.IsOpenable("file:///tmp/note.txt"));
        Assert.False(GridText.IsOpenable("file://example.com/tmp/note.txt"));
    }

    [Fact]
    public void UrlAt_JoinsAWrappedUrl()
    {
        string[][] rows =
        [
            Cells("https://ex", 10),
            Cells("ample.com/", 10),
            Cells("wrap", 10),
        ];
        Assert.Equal("https://example.com/wrap", GridText.UrlAt(rows, [true, true, false], 0, 2));
    }

    private static string[] Cells(string text, int width)
    {
        var cells = new string[width];
        for (int i = 0; i < width; i++)
        {
            cells[i] = i < text.Length ? text[i].ToString() : " ";
        }

        return cells;
    }
}
