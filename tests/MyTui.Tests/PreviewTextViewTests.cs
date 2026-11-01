using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class PreviewTextViewTests
{
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
