using System.Drawing;
using System.Reflection;
using MyTui.Sessions;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class PreviewRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalEditsRefreshThePreviewAndPreserveScroll(bool markdown)
    {
        using var workspace = new TestWorkspace(markdown);
        View view = workspace.FileView;
        Assert.True(view.SetFocus());
        view.Viewport = view.Viewport with { X = markdown ? 0 : 4, Y = 12 };
        Point previous = view.Viewport.Location;
        Assert.Equal(12, previous.Y);

        string updated = workspace.Original.Replace("line", "edit");
        workspace.Write(updated);
        workspace.Tick();

        Assert.Equal(updated, workspace.PreviewText);
        Assert.Equal(previous, view.Viewport.Location);
        Assert.True(view.HasFocus);
        workspace.Tick();
        Assert.Equal(previous, view.Viewport.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendingToAFileDoesNotFollowTheBottom(bool markdown)
    {
        using var workspace = new TestWorkspace(markdown);
        View view = workspace.FileView;
        view.Viewport = view.Viewport with { Y = view.GetContentHeight() - view.Viewport.Height };
        int previous = view.Viewport.Y;
        Assert.True(previous > 0);

        string updated = workspace.Original + "\n\n" + workspace.Original;
        workspace.Write(updated);
        workspace.Tick();

        Assert.Equal(updated, workspace.PreviewText);
        Assert.Equal(previous, view.Viewport.Y);
        Assert.True(view.Viewport.Y < view.GetContentHeight() - view.Viewport.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtomicSavesAndDeletionRecoverAndShorterFilesClampScroll(bool markdown)
    {
        using var workspace = new TestWorkspace(markdown);
        View view = workspace.FileView;
        view.Viewport = view.Viewport with { X = markdown ? 0 : 4, Y = 12 };
        Point previous = view.Viewport.Location;
        string updated = workspace.Original.Replace("line", "save");
        string replacement = workspace.Path + ".tmp";
        File.WriteAllText(replacement, updated);
        File.SetLastWriteTimeUtc(replacement, DateTime.UtcNow.AddSeconds(5));
        File.Move(replacement, workspace.Path, overwrite: true);
        workspace.Tick();

        Assert.Equal(updated, workspace.PreviewText);
        Assert.Equal(previous, view.Viewport.Location);

        File.Delete(workspace.Path);
        workspace.Tick();
        Assert.Equal("File not found.", workspace.Note.Text);
        Assert.True(workspace.Note.Visible);

        workspace.Write(updated);
        workspace.Tick();
        Assert.Equal(updated, workspace.PreviewText);
        Assert.Equal(previous, view.Viewport.Location);
        Assert.False(workspace.Note.Visible);

        workspace.Write("short");
        workspace.Tick();
        Assert.Equal("short", workspace.PreviewText);
        Assert.Equal(Point.Empty, view.Viewport.Location);
    }

    [Fact]
    public void HiddenPreviewsCatchUpAndSelectingAnotherFileResetsScroll()
    {
        using var workspace = new TestWorkspace(markdown: false);
        View view = workspace.FileView;
        view.Viewport = view.Viewport with { X = 4, Y = 12 };
        Point previous = view.Viewport.Location;
        workspace.Screen.ExecuteCommand("preview");
        Assert.False(workspace.Preview.Visible);
        workspace.Write(workspace.Original.Replace("line", "edit"));
        workspace.Tick();
        Assert.Equal(workspace.Original, workspace.PreviewText);

        workspace.Screen.ExecuteCommand("preview");
        workspace.Tick();
        Assert.Contains("edit", workspace.PreviewText);
        Assert.Equal(previous, view.Viewport.Location);

        workspace.Select("other.txt");
        Assert.Equal("another file", workspace.PreviewText);
        Assert.Equal(Point.Empty, view.Viewport.Location);
        workspace.Write("old selection changed");
        workspace.Tick();
        Assert.Equal("another file", workspace.PreviewText);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-preview-refresh").FullName;
        private readonly IApplication _app = Application.Create();
        private readonly View _parent;
        private readonly bool _markdown;
        private int _revision;

        internal TestWorkspace(bool markdown)
        {
            _markdown = markdown;
            Path = System.IO.Path.Combine(_directory, markdown ? "test.md" : "test.txt");
            Original = string.Join("\n\n", Enumerable.Range(1, 80).Select(n => $"line {n:000} {new string('x', 80)}"));
            File.WriteAllText(Path, Original);
            File.WriteAllText(System.IO.Path.Combine(_directory, "other.txt"), "another file");
            string state = System.IO.Path.Combine(_directory, "state.json");
            SessionManager.Load(state).Create("test", _directory, "/bin/sh", []);
            Screen = new SessionScreen(_app);
            _parent = new View { App = _app, Width = 120, Height = 30, CanFocus = true };
            _parent.Add(Screen.Sessions, Screen.Content);
            _parent.Layout();
            Screen.Start(state);
            Screen.ExecuteCommand("files");
            Select(System.IO.Path.GetFileName(Path));
        }

        internal SessionScreen Screen { get; }
        internal string Path { get; }
        internal string Original { get; }
        internal FrameView Preview => Screen.Content.SubViews.OfType<FrameView>().Single();
        internal PreviewTextView Text => Preview.SubViews.OfType<PreviewTextView>().Single();
        internal PreviewMarkdown Markdown => Preview.SubViews.OfType<PreviewMarkdown>().Single();
        internal View FileView => _markdown ? Markdown : Text;
        internal string PreviewText => _markdown ? Markdown.Text : Text.Content;
        internal Label Note => Preview.SubViews.OfType<Label>().Single(label => label.Text != " Copied ");

        internal void Write(string content)
        {
            File.WriteAllText(Path, content);
            File.SetLastWriteTimeUtc(Path, DateTime.UtcNow.AddSeconds(++_revision * 10));
        }

        internal void Select(string name)
        {
            TreeView<FileNode> tree = Screen.Content.SubViews.OfType<Tabs>().Single()
                .SubViews.SelectMany(view => view.SubViews).OfType<TreeView<FileNode>>().Single();
            tree.GoTo(Workspace.Children(_directory, _directory).Single(node => node.Name == name));
            _parent.Layout();
        }

        internal void Tick()
        {
            typeof(SessionScreen).GetField("_nextPreviewCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Screen, 0L);
            Assert.Equal(true, typeof(SessionScreen).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Screen, null));
            _parent.Layout();
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
