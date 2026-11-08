using System.Drawing;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class SessionSplitterTests
{
    [Theory]
    [InlineData(28, 80, 28)]
    [InlineData(1, 80, 18)]
    [InlineData(100, 80, 60)]
    [InlineData(50, 120, 50)]
    [InlineData(28, 30, 15)]
    [InlineData(28, 10, 5)]
    [InlineData(28, 1, 0)]
    [InlineData(28, 0, 0)]
    public void WidthKeepsRoomForBothPanels(int preferred, int available, int expected)
    {
        Assert.Equal(expected, SessionSplitter.ClampWidth(preferred, available));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherBorderDragsAndReleaseStopsResizing(bool contentBorder)
    {
        using IApplication app = Application.Create();
        using var parent = new View { App = app, Width = 100, Height = 20 };
        var sessions = new FrameView { Width = 28, Height = Dim.Fill() };
        var content = new FrameView { X = Pos.Right(sessions), Width = Dim.Fill(), Height = Dim.Fill() };
        parent.Add(sessions, content);
        parent.Layout();
        int width = 28;
        using var splitter = new SessionSplitter(sessions, content, value =>
        {
            width = value;
            sessions.Width = value;
            parent.Layout();
        }) { Active = true };
        View border = (contentBorder ? content : sessions).Border.GetOrCreateView();
        Point start = border.ViewportToScreen(new Point(contentBorder ? 0 : border.Viewport.Width - 1, 5));

        Assert.True(Send(border, start, MouseFlags.LeftButtonPressed));
        Assert.True(app.Mouse.IsGrabbed(border));
        Assert.True(Send(border, start with { X = start.X + 12 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport));
        Assert.Equal(40, width);
        // Coordinates stay screen-relative even though the border moves with the resize.
        Assert.True(Send(border, start with { X = start.X + 18 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport));
        Assert.Equal(46, width);
        Assert.True(Send(border, start with { X = start.X + 18 }, MouseFlags.LeftButtonReleased));
        Assert.False(app.Mouse.IsGrabbed());
        Send(border, start with { X = start.X + 30 }, MouseFlags.PositionReport);
        Assert.Equal(46, width);
    }

    [Fact]
    public void CaptionsAndInteriorDoNotStartADrag()
    {
        using var sessions = new FrameView { Width = 28, Height = 20 };
        using var content = new FrameView { Width = 50, Height = 20 };
        sessions.Layout();
        content.Layout();
        int calls = 0;
        using var splitter = new SessionSplitter(sessions, content, _ => calls++) { Active = true };
        View border = sessions.Border.GetOrCreateView();
        foreach (Point position in new[] { new Point(27, 0), new Point(27, 19), new Point(10, 5) })
        {
            Point screen = border.ViewportToScreen(position);
            Send(border, screen, MouseFlags.LeftButtonPressed);
            Send(border, screen with { X = screen.X + 5 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
            Send(border, screen, MouseFlags.LeftButtonReleased);
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public void DeactivatingCancelsAnInProgressDrag()
    {
        using var sessions = new FrameView { Width = 28, Height = 20 };
        using var content = new FrameView { Width = 50, Height = 20 };
        sessions.Layout();
        int calls = 0;
        using var splitter = new SessionSplitter(sessions, content, _ => calls++) { Active = true };
        View border = sessions.Border.GetOrCreateView();
        Point start = border.ViewportToScreen(new Point(27, 5));
        Send(border, start, MouseFlags.LeftButtonPressed);
        splitter.Active = false;
        splitter.Active = true;
        Send(border, start with { X = start.X + 5 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void SessionScreenKeepsDraggedWidthAcrossLayoutsAndHostResizes()
    {
        using IApplication app = Application.Create();
        using var screen = new SessionScreen(app);
        using var parent = new View { Width = 100, Height = 20 };
        parent.Add(screen.Sessions, screen.Content);
        parent.Layout();
        View border = screen.Sessions.Border.GetOrCreateView();
        Point start = border.ViewportToScreen(new Point(screen.Sessions.Frame.Width - 1, 5));
        Send(border, start, MouseFlags.LeftButtonPressed);
        Send(border, start with { X = start.X + 16 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
        Send(border, start with { X = start.X + 16 }, MouseFlags.LeftButtonReleased);
        Assert.Equal(44, screen.Sessions.Frame.Width);
        Assert.Equal(44, screen.Content.Frame.X);
        Assert.Equal(56, screen.Content.Frame.Width);

        Assert.True(screen.HandleChord(Key.F4));
        Assert.False(screen.Sessions.Visible);
        Assert.True(screen.HandleChord(Key.F4));
        Assert.Equal(44, screen.Sessions.Frame.Width);

        PanelCaption caption = screen.Sessions.Border.GetOrCreateView().SubViews.OfType<PanelCaption>().Single();
        caption.Activate(0);
        Assert.Equal(100, screen.Sessions.Frame.Width);
        caption.Activate(0);
        Assert.Equal(44, screen.Sessions.Frame.Width);

        parent.Width = 50;
        parent.Layout();
        Assert.Equal(30, screen.Sessions.Frame.Width);
        Assert.Equal(20, screen.Content.Frame.Width);
        parent.Width = 100;
        parent.Height = 30;
        parent.Layout();
        Assert.Equal(44, screen.Sessions.Frame.Width);
        Assert.Equal(30, screen.Sessions.Frame.Height);
    }

    private static bool? Send(View view, Point position, MouseFlags flags) => view.NewMouseEvent(new Mouse
    {
        View = view,
        ScreenPosition = position,
        Position = view.ScreenToViewport(position),
        Flags = flags,
    });
}
