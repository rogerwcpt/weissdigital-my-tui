using System.Drawing;
using MyTui.Sessions;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class LayoutCommandTests
{
    [Fact]
    public void CenterPanelCanBeHiddenAndShownWithoutChangingTheTab()
    {
        using var workspace = new TestWorkspace();
        workspace.Run("Show Files");
        workspace.Run("Toggle Center Panel");

        Assert.False(workspace.Screen.Content.Visible);
        Assert.True(workspace.Screen.Sessions.Visible);
        Assert.True(workspace.Screen.Sessions.HasFocus);

        workspace.Run("Toggle Center Panel");

        Assert.True(workspace.Screen.Content.Visible);
        Assert.True(workspace.Tabs.Visible);
        Assert.Equal("Files", workspace.Tabs.Value!.Title);
        Assert.True(workspace.Tabs.Value.HasFocus);
        Assert.Equal(SessionTab.Files, workspace.SavedTab);
    }

    [Theory]
    [InlineData("Sessions")]
    [InlineData("Preview")]
    public void CenterPanelCanBeRevealedWhenAnotherPanelIsMaximized(string panel)
    {
        using var workspace = new TestWorkspace();
        workspace.Maximize(panel);

        workspace.Run("Toggle Center Panel");

        Assert.True(workspace.Screen.Content.Visible);
        Assert.True(workspace.Tabs.Visible);
        Assert.True(workspace.Tabs.Value!.HasFocus);
    }

    [Theory]
    [InlineData("Sessions")]
    [InlineData("Preview")]
    [InlineData("Center")]
    public void RestoreLayoutClearsHiddenAndMaximizedPanelsAndResetsWidth(string panel)
    {
        using var workspace = new TestWorkspace();
        workspace.Run("Show Files");
        workspace.ResizeSessions();
        Assert.Equal(44, workspace.Screen.Sessions.Frame.Width);
        workspace.Run("Toggle Sessions Panel");
        workspace.Run("Toggle Preview Panel");
        workspace.Maximize(panel);

        workspace.Run("Restore layout");

        workspace.AssertDefaultLayout();
        Assert.Equal("Files", workspace.Tabs.Value!.Title);
        Assert.Equal(SessionTab.Files, workspace.SavedTab);
        workspace.Run("Restore layout");
        workspace.AssertDefaultLayout();
    }

    [Theory]
    [InlineData("Sessions", "Show Terminal", "Terminal")]
    [InlineData("Sessions", "Show Files", "Files")]
    [InlineData("Preview", "Show Terminal", "Terminal")]
    [InlineData("Preview", "Show Files", "Files")]
    public void ShowTabRestoresDefaultLayoutBeforeSelectingAndFocusingTheTab(
        string panel, string command, string title)
    {
        SessionTab tab = title == "Files" ? SessionTab.Files : SessionTab.Terminal;
        using var workspace = new TestWorkspace();
        workspace.Run(command == "Show Files" ? "Show Terminal" : "Show Files");
        workspace.ResizeSessions();
        workspace.Run("Toggle Sessions Panel");
        workspace.Maximize(panel);

        workspace.Run(command);

        workspace.AssertDefaultLayout();
        Assert.Equal(title, workspace.Tabs.Value!.Title);
        Assert.True(workspace.Tabs.Value.HasFocus);
        Assert.Equal(tab, workspace.SavedTab);
        // Showing a terminal is not the same as starting its process.
        Assert.Null(workspace.Grid.Session);
        workspace.Run(command);
        Assert.Equal(title, workspace.Tabs.Value.Title);
        Assert.Equal(tab, workspace.SavedTab);
    }

    [Theory]
    [InlineData("Show Terminal", "Terminal")]
    [InlineData("Show Files", "Files")]
    public void ShowTabRevealsAMinimizedCenterPanel(string command, string title)
    {
        using var workspace = new TestWorkspace();
        workspace.Run("Toggle Center Panel");

        workspace.Run(command);

        Assert.True(workspace.Screen.Content.Visible);
        Assert.True(workspace.Tabs.Visible);
        Assert.Equal(title, workspace.Tabs.Value!.Title);
        Assert.True(workspace.Tabs.Value.HasFocus);
    }

    [Theory]
    [InlineData("Show Terminal", "Terminal")]
    [InlineData("Show Files", "Files")]
    public void ShowTabPreservesTheLayoutWhenTheTabsAreAlreadyVisible(string command, string title)
    {
        using var workspace = new TestWorkspace();
        workspace.ResizeSessions();
        workspace.Run("Toggle Preview Panel");

        workspace.Run(command);

        Assert.True(workspace.Preview.Visible);
        Assert.Equal(44, workspace.Screen.Sessions.Frame.Width);
        Assert.Equal(title, workspace.Tabs.Value!.Title);
        workspace.Run("Toggle Sessions Panel");
        workspace.Run(command);
        Assert.False(workspace.Screen.Sessions.Visible);
        Assert.True(workspace.Preview.Visible);
    }

    [Theory]
    [InlineData("Show Terminal", "Terminal")]
    [InlineData("Show Files", "Files")]
    public void ShowTabPreservesAMaximizedCenterPanel(string command, string title)
    {
        using var workspace = new TestWorkspace();
        workspace.Maximize("Center");

        workspace.Run(command);

        Assert.False(workspace.Screen.Sessions.Visible);
        Assert.True(workspace.Tabs.Visible);
        Assert.Equal(title, workspace.Tabs.Value!.Title);
        Assert.True(workspace.Tabs.Value.HasFocus);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-layout").FullName;
        private readonly IApplication _app = Application.Create();
        private readonly View _parent;
        private string StatePath => Path.Combine(_directory, "state.json");

        internal TestWorkspace()
        {
            SessionManager.Load(StatePath).Create("test", _directory, "/bin/sh", []);
            Screen = new SessionScreen(_app);
            _parent = new View { App = _app, Width = 100, Height = 30, CanFocus = true };
            _parent.Add(Screen.Sessions, Screen.Content);
            _parent.Layout();
            Screen.Start(StatePath);
        }

        internal SessionScreen Screen { get; }
        internal Tabs Tabs => Screen.Content.SubViews.OfType<Tabs>().Single();
        internal FrameView Preview => Screen.Content.SubViews.OfType<FrameView>().Single();
        internal CellGridView Grid => Tabs.SubViews.SelectMany(view => view.SubViews).OfType<CellGridView>().Single();
        internal SessionTab SavedTab => SessionManager.Load(StatePath).Focused!.Tab;

        internal void Run(string title) => Screen.ExecuteCommand(
            Assert.Single(CommandPalette.Matches("", filesActive: true), entry => entry.Title == title).Id);

        internal void Maximize(string panel)
        {
            if (panel == "Preview")
            {
                Run("Toggle Preview Panel");
            }

            FrameView frame = panel switch
            {
                "Sessions" => Screen.Sessions,
                "Preview" => Preview,
                _ => Screen.Content,
            };
            frame.Border.GetOrCreateView().SubViews.OfType<PanelCaption>().Single().Activate(0);
        }

        internal void ResizeSessions()
        {
            View border = Screen.Sessions.Border.GetOrCreateView();
            Point start = border.ViewportToScreen(new Point(Screen.Sessions.Frame.Width - 1, 5));
            Send(start, MouseFlags.LeftButtonPressed);
            Send(start with { X = start.X + 16 }, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport);
            Send(start with { X = start.X + 16 }, MouseFlags.LeftButtonReleased);

            void Send(Point point, MouseFlags flags) => border.NewMouseEvent(new Mouse
            {
                View = border,
                ScreenPosition = point,
                Position = border.ScreenToViewport(point),
                Flags = flags,
            });
        }

        internal void AssertDefaultLayout()
        {
            Assert.True(Screen.Sessions.Visible);
            Assert.True(Screen.Content.Visible);
            Assert.True(Tabs.Visible);
            Assert.False(Preview.Visible);
            Assert.Equal(SessionSplitter.DefaultWidth, Screen.Sessions.Frame.Width);
            Assert.Equal(SessionSplitter.DefaultWidth, Screen.Content.Frame.X);
            Assert.All(new[] { Screen.Sessions, Screen.Content, Preview }, frame =>
                Assert.False(frame.Border.GetOrCreateView().SubViews.OfType<PanelCaption>().Single().Maximized));
        }

        public void Dispose()
        {
            Screen.Dispose();
            _parent.Dispose();
            _app.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
