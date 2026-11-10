using MyTui.UI;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace MyTui.Tests;

public class PreviewTextViewTests
{
    [Fact]
    public void DragSelectsAcrossLinesInEitherDirection()
    {
        using var view = new PreviewTextView { Content = "first\nsecond\nthird" };
        view.Layout(new System.Drawing.Size(30, 10));

        Send(view, 2, 0, MouseFlags.LeftButtonPressed);
        Send(view, 3, 1, MouseFlags.PositionReport | MouseFlags.LeftButtonPressed);
        Send(view, 3, 1, MouseFlags.LeftButtonReleased);
        Assert.Equal("rst\nsec", view.SelectedText);

        Send(view, 3, 1, MouseFlags.LeftButtonPressed);
        Send(view, 2, 0, MouseFlags.LeftButtonReleased);
        Assert.Equal("rst\nsec", view.SelectedText);
    }

    [Fact]
    public void SelectionUsesContentCoordinatesAndPreservesTabsAndUnicode()
    {
        using var view = new PreviewTextView { Content = "\t😀hello " + new string('x', 80) };
        view.Layout(new System.Drawing.Size(20, 8));
        Send(view, 0, 0, MouseFlags.LeftButtonPressed);
        Send(view, 5, 0, MouseFlags.LeftButtonReleased);
        Assert.Equal("\t😀", view.SelectedText);

        view.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(4, view.Viewport.X);
        Send(view, 0, 0, MouseFlags.LeftButtonPressed);
        Send(view, 3, 0, MouseFlags.LeftButtonReleased);
        Assert.Equal("😀he", view.SelectedText);
    }

    [Fact]
    public void SelectAllAndShiftArrowsDoNotChangeTheReadOnlyContent()
    {
        using var view = new PreviewTextView { Content = "😀hello\r\nworld" };
        view.Layout(new System.Drawing.Size(30, 10));
        Assert.True(view.NewKeyDownEvent(Key.CursorRight.WithShift));
        Assert.Equal("😀", view.SelectedText);
        Assert.True(view.NewKeyDownEvent(Key.A.WithCtrl));
        Assert.Equal("😀hello\nworld", view.SelectedText);
        Assert.Equal(view.Content, view.SelectedText);

        view.Content = "another file";
        Assert.Empty(view.SelectedText);
    }

    [Fact]
    public void DoubleAndTripleClickSelectWordAndLine()
    {
        using var view = new PreviewTextView { Content = "one two\nthree" };
        view.Layout(new System.Drawing.Size(30, 10));
        Send(view, 5, 0, MouseFlags.LeftButtonDoubleClicked);
        Assert.Equal("two", view.SelectedText);
        Send(view, 5, 0, MouseFlags.LeftButtonTripleClicked);
        Assert.Equal("one two\n", view.SelectedText);
    }

    [Fact]
    public void FinishedSelectionsArePublishedButPlainClicksAreNot()
    {
        var published = new List<string>();
        using var view = new PreviewTextView { Content = "one two\nthree", Selected = published.Add };
        view.Layout(new System.Drawing.Size(30, 10));

        Send(view, 1, 0, MouseFlags.LeftButtonPressed);
        Send(view, 1, 0, MouseFlags.LeftButtonReleased);
        Assert.Empty(published);

        Send(view, 0, 0, MouseFlags.LeftButtonPressed);
        Send(view, 3, 0, MouseFlags.PositionReport | MouseFlags.LeftButtonPressed);
        Assert.Empty(published);
        Send(view, 3, 0, MouseFlags.LeftButtonReleased);
        Send(view, 5, 0, MouseFlags.LeftButtonDoubleClicked);
        Send(view, 0, 1, MouseFlags.LeftButtonTripleClicked);
        Assert.Equal(["one", "two", "three"], published);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterCopiesAndClearsTheSelection(bool markdown)
    {
        var published = new List<string>();
        using View view = markdown
            ? new PreviewMarkdown { Text = "hello world", Width = 30, Height = 10, Selected = published.Add }
            : new PreviewTextView { Content = "hello world", Selected = published.Add };
        view.Layout(new System.Drawing.Size(30, 10));

        view.NewKeyDownEvent(Key.Enter);
        Assert.Empty(published);
        Assert.True(view.NewKeyDownEvent(Key.A.WithCtrl));
        Assert.True(view.NewKeyDownEvent(Key.Enter));
        Assert.Equal(["hello world"], published);
        Assert.True(string.IsNullOrEmpty(markdown ? ((PreviewMarkdown)view).SelectedText : ((PreviewTextView)view).SelectedText));
    }

    private static void Send(PreviewTextView view, int x, int y, MouseFlags flags) =>
        view.NewMouseEvent(new Mouse
        {
            View = view,
            Position = new System.Drawing.Point(x, y),
            ScreenPosition = view.ViewportToScreen(new System.Drawing.Point(x, y)),
            Flags = flags,
        });

    [Fact]
    public void LongSourceShowsAVerticalScrollbarAndPageDownMovesIt()
    {
        var view = new PreviewTextView();
        view.Content = string.Join('\n', Enumerable.Range(1, 40).Select(n => $"line {n:000}"));

        Assert.True(view.Layout(new System.Drawing.Size(30, 10)));
        Assert.Equal(40, view.GetContentHeight());
        Assert.True(view.VerticalScrollBar.Visible);
        Assert.False(view.HorizontalScrollBar.Visible);
        Assert.Equal(0, view.Viewport.Y);

        Assert.True(view.NewKeyDownEvent(Key.PageDown));
        Assert.True(view.Viewport.Y > 0);

        Assert.True(view.NewKeyDownEvent(Key.Home));
        Assert.Equal(0, view.Viewport.Y);
    }

    [Fact]
    public void ShortSourceHidesTheScrollbar()
    {
        var view = new PreviewTextView();
        view.Content = "one\ntwo";

        Assert.True(view.Layout(new System.Drawing.Size(30, 10)));
        Assert.False(view.VerticalScrollBar.Visible);
        Assert.False(view.HorizontalScrollBar.Visible);
    }

    [Fact]
    public void WideLineShowsAHorizontalScrollbar()
    {
        var view = new PreviewTextView();
        view.Content = "\t" + new string('x', 80);

        Assert.True(view.Layout(new System.Drawing.Size(20, 8)));
        Assert.True(view.GetContentWidth() > view.Viewport.Width);
        Assert.True(view.HasHorizontalBar);
        Assert.Equal(1, view.Padding.Thickness.Bottom);
        Assert.False(view.VerticalScrollBar.Visible);

        Assert.True(view.NewKeyDownEvent(Key.CursorRight));
        Assert.Equal(4, view.Viewport.X);
    }
}
