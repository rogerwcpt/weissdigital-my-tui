using MyTui.Sessions;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

[Collection("HostTheme")]
public class CommanderKeyTests
{
    [Fact]
    public void F6AndF8TargetSessionsEvenWhenTheFilesTabIsSelected()
    {
        using var workspace = new TestWorkspace();
        workspace.Screen.ExecuteCommand("files");
        workspace.SessionsList.SetFocus();
        workspace.PressWithDialog(Key.F6, dialog =>
        {
            Assert.Equal("Rename session", dialog.Title);
            dialog.SubViews.OfType<TextField>().Single().Text = "renamed session";
            dialog.NewKeyDownEvent(Key.Enter);
        });

        Assert.Equal("renamed session", SessionManager.Load(workspace.StatePath).Focused!.Name);
        Assert.True(File.Exists(workspace.FilePath));
        workspace.SessionsList.SetFocus();
        Assert.True(workspace.SessionsList.NewKeyDownEvent(Key.F8));
        Assert.Empty(SessionManager.Load(workspace.StatePath).Sessions);
        Assert.True(File.Exists(workspace.FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F6RenamesFilesAndNestedFilesAndKeepsTheSelection(bool nested)
    {
        using var workspace = new TestWorkspace();
        string source = nested ? workspace.NestedFilePath : workspace.FilePath;
        workspace.Select(source);
        workspace.PressWithDialog(Key.F6, dialog =>
        {
            Assert.Equal("Rename file", dialog.Title);
            dialog.SubViews.OfType<TextField>().Single().Text = "renamed.txt";
            dialog.NewKeyDownEvent(Key.Enter);
        });

        string target = Path.Combine(Path.GetDirectoryName(source)!, "renamed.txt");
        Assert.False(File.Exists(source));
        Assert.Equal("preview text", File.ReadAllText(target));
        Assert.Equal(target, workspace.Tree.SelectedObject!.Path);
        Assert.Equal("test session", SessionManager.Load(workspace.StatePath).Focused!.Name);
        Assert.True(workspace.Tree.HasFocus);
        Assert.Equal("preview text", workspace.Text.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F8FileDeleteDefaultsToCancelAndEscapeCancels(bool escape)
    {
        using var workspace = new TestWorkspace();
        workspace.Select(workspace.FilePath);
        workspace.PressWithDialog(Key.F8, dialog =>
        {
            Assert.Equal("Delete file", dialog.Title);
            dialog.NewKeyDownEvent(escape ? Key.Esc : Key.Enter);
        });

        Assert.True(File.Exists(workspace.FilePath));
        Assert.Single(SessionManager.Load(workspace.StatePath).Sessions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F8DeletesOnlyTheSelectedFileOrFolderAfterConfirmation(bool directory)
    {
        using var workspace = new TestWorkspace();
        string source = directory ? Path.GetDirectoryName(workspace.NestedFilePath)! : workspace.FilePath;
        workspace.Select(source);
        workspace.PressWithDialog(Key.F8, dialog =>
        {
            Assert.Equal(directory ? "Delete folder" : "Delete file", dialog.Title);
            Assert.Contains("cannot be undone", dialog.SubViews.OfType<Label>().Single().Text);
            if (directory)
            {
                Assert.Contains("All contents", dialog.SubViews.OfType<Label>().Single().Text);
            }

            dialog.Result = 1;
            workspace.App.RequestStop(dialog);
        });

        Assert.False(Path.Exists(source));
        Assert.True(Path.Exists(directory ? workspace.FilePath : workspace.NestedFilePath));
        Assert.Single(SessionManager.Load(workspace.StatePath).Sessions);
        Assert.True(workspace.Tree.HasFocus);
        Assert.NotEqual(source, workspace.Tree.SelectedObject?.Path);
    }

    [Theory]
    [InlineData("Sessions")]
    [InlineData("Files")]
    [InlineData("Terminal")]
    public void F3TogglesPreviewFromEveryPanelAndBackFromPreview(string panel)
    {
        using var workspace = new TestWorkspace();
        View focused;
        if (panel == "Files")
        {
            workspace.Select(workspace.FilePath);
            workspace.Screen.ExecuteCommand("preview"); // File selection opens Preview; start closed.
            focused = workspace.Tree;
        }
        else if (panel == "Terminal")
        {
            workspace.Screen.ExecuteCommand("terminal");
            focused = workspace.Grid;
        }
        else
        {
            focused = workspace.SessionsList;
        }

        focused.SetFocus();
        Assert.False(workspace.Preview.Visible);
        Assert.True(focused.NewKeyDownEvent(Key.F3));
        Assert.True(workspace.Preview.Visible);
        Assert.True(workspace.Preview.HasFocus);
        Assert.True(workspace.Preview.NewKeyDownEvent(Key.F3));
        Assert.False(workspace.Preview.Visible);
    }

    [Fact]
    public void RenameAndDeleteDoNotTargetPanelsBehindTerminalOrPreview()
    {
        using var workspace = new TestWorkspace();
        workspace.Screen.ExecuteCommand("terminal");
        workspace.Grid.SetFocus();
        Assert.False(workspace.Screen.HandleChord(Key.F6, inTerminal: true));
        Assert.False(workspace.Screen.HandleChord(Key.F8, inTerminal: true));
        Assert.False(workspace.Screen.HandleChord(Key.F6));
        Assert.False(workspace.Screen.HandleChord(Key.F8));

        workspace.Select(workspace.FilePath);
        workspace.Screen.ExecuteCommand("preview");
        workspace.Screen.ExecuteCommand("preview");
        Assert.True(workspace.Preview.HasFocus);
        Assert.False(workspace.Screen.HandleChord(Key.F6));
        Assert.False(workspace.Screen.HandleChord(Key.F8));
        Assert.True(File.Exists(workspace.FilePath));
        Assert.Single(SessionManager.Load(workspace.StatePath).Sessions);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-commander").FullName;
        private readonly Window _window;
        private readonly SessionToken _token;

        internal TestWorkspace()
        {
            File.WriteAllText(FilePath, "preview text");
            Directory.CreateDirectory(Path.GetDirectoryName(NestedFilePath)!);
            File.WriteAllText(NestedFilePath, "preview text");
            SessionManager.Load(StatePath).Create("test session", _directory, "/bin/sh", []);
            App = Application.Create();
            App.Init(DriverRegistry.Names.ANSI);
            App.Driver!.SetScreenSize(100, 30);
            Screen = new SessionScreen(App);
            _window = new Window();
            Screen.AttachTitle(_window);
            _window.Add(Screen.Sessions, Screen.Content);
            _token = App.Begin(_window)!;
            Screen.Start(StatePath);
            App.LayoutAndDraw();
        }

        internal IApplication App { get; }
        internal SessionScreen Screen { get; }
        internal string StatePath => Path.Combine(_directory, "state.json");
        internal string FilePath => Path.Combine(_directory, "notes.txt");
        internal string NestedFilePath => Path.Combine(_directory, "folder", "nested.txt");
        internal ListView SessionsList => Screen.Sessions.SubViews.OfType<ListView>().Single();
        internal Tabs Tabs => Screen.Content.SubViews.OfType<Tabs>().Single();
        internal TreeView<FileNode> Tree => Tabs.SubViews.SelectMany(view => view.SubViews).OfType<TreeView<FileNode>>().Single();
        internal CellGridView Grid => Tabs.SubViews.SelectMany(view => view.SubViews).OfType<CellGridView>().Single();
        internal FrameView Preview => Screen.Content.SubViews.OfType<FrameView>().Single();
        internal PreviewTextView Text => Preview.SubViews.OfType<PreviewTextView>().Single();

        internal void Select(string path)
        {
            Screen.ExecuteCommand("files");
            if (Path.GetDirectoryName(path) != _directory)
            {
                Tree.Expand(Workspace.Children(_directory, _directory).Single(node => node.Name == "folder"));
            }

            Tree.GoTo(Workspace.Children(_directory, Path.GetDirectoryName(path)!).Single(node => node.Path == path));
            Tree.SetFocus();
        }

        internal void PressWithDialog(Key key, Action<Dialog> respond)
        {
            bool seen = false;
            object timeout = App.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
            {
                var dialog = Assert.IsType<Dialog>(App.TopRunnableView);
                seen = true;
                respond(dialog);
                return false;
            })!;
            try
            {
                Assert.True(_window.NewKeyDownEvent(key));
                Assert.True(seen, "Expected a confirmation or rename dialog.");
            }
            finally
            {
                App.RemoveTimeout(timeout);
            }
        }

        public void Dispose()
        {
            Screen.Dispose();
            App.End(_token);
            _window.Dispose();
            App.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
