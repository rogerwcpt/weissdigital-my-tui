using MyTui.Sessions;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class ScriptCommandTests
{
    [UnixFact]
    public void HighlightDoesNotExecuteAndExplicitRunShowsOutputWithoutStartingTheTerminal()
    {
        using var workspace = new TestWorkspace("printf 'script stdout'; printf 'script stderr' >&2; touch ran");
        workspace.Screen.ExecuteCommand("files");
        workspace.Select("test.sh");
        Assert.False(File.Exists(workspace.Marker));
        Assert.Contains("printf", workspace.Text.Content);

        workspace.Screen.ExecuteCommand("terminal");
        workspace.Screen.ExecuteCommand("run-script");
        Assert.False(File.Exists(workspace.Marker));
        workspace.Screen.ExecuteCommand("files");
        workspace.Screen.ExecuteCommand("run-script");
        workspace.WaitForStatus("exited 0");

        Assert.True(File.Exists(workspace.Marker));
        Assert.True(workspace.Preview.Visible);
        Assert.Contains("script stdout", workspace.Text.Content);
        Assert.Contains("script stderr", workspace.Text.Content);
        Assert.Contains("[exited 0]", workspace.Text.Content);
        Assert.Null(workspace.Grid.Session);

        workspace.Screen.ExecuteCommand("files");
        workspace.Select("notes.txt");
        Assert.Equal("file preview", workspace.Text.Content);
        Assert.StartsWith("Preview", workspace.Preview.Title);
        workspace.Screen.ExecuteCommand("script-output");
        Assert.Contains("script stdout", workspace.Text.Content);
        Assert.StartsWith("Output", workspace.Preview.Title);
        Assert.True(workspace.Text.HasFocus);

        workspace.Screen.ExecuteCommand("center");
        Assert.False(workspace.Screen.Content.Visible);
        workspace.Screen.ExecuteCommand("script-output");
        Assert.True(workspace.Screen.Content.Visible);
        Assert.True(workspace.Preview.Visible);
        Assert.True(workspace.Text.HasFocus);
    }

    [UnixFact]
    public void NonScriptsAreNotRunAndARunningScriptCanBeStoppedFromTerminal()
    {
        using var workspace = new TestWorkspace("printf 'live output'; touch ran; sleep 60");
        workspace.Screen.ExecuteCommand("files");
        workspace.Select("notes.txt");
        workspace.Screen.ExecuteCommand("run-script");
        Assert.False(File.Exists(workspace.Marker));
        Assert.Equal("file preview", workspace.Text.Content);

        workspace.Select("test.sh");
        workspace.Screen.ExecuteCommand("run-script");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(workspace.Marker), 5000));
        workspace.Screen.ExecuteCommand("terminal");
        workspace.Screen.ExecuteCommand("stop-script");
        workspace.WaitForStatus("stopped");
        Assert.Contains("live output", workspace.Text.Content);
        Assert.Null(workspace.Grid.Session);
        Assert.Equal("Terminal", workspace.Tabs.Value!.Title);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-script-command").FullName;
        private readonly IApplication _app = Application.Create();
        private readonly View _parent;

        internal TestWorkspace(string script)
        {
            File.WriteAllText(Path.Combine(_directory, "test.sh"), script);
            File.WriteAllText(Path.Combine(_directory, "notes.txt"), "file preview");
            string state = Path.Combine(_directory, "state.json");
            SessionManager.Load(state).Create("test", _directory, "/bin/sh", []);
            Screen = new SessionScreen(_app);
            _parent = new View { App = _app, Width = 100, Height = 30, CanFocus = true };
            _parent.Add(Screen.Sessions, Screen.Content);
            _parent.Layout();
            Screen.Start(state);
        }

        internal SessionScreen Screen { get; }
        internal string Marker => Path.Combine(_directory, "ran");
        internal Tabs Tabs => Screen.Content.SubViews.OfType<Tabs>().Single();
        internal FrameView Preview => Screen.Content.SubViews.OfType<FrameView>().Single();
        internal PreviewTextView Text => Preview.SubViews.OfType<PreviewTextView>().Single();
        internal CellGridView Grid => Tabs.SubViews.SelectMany(view => view.SubViews).OfType<CellGridView>().Single();

        internal void Select(string name)
        {
            TreeView<FileNode> tree = Tabs.SubViews.SelectMany(view => view.SubViews).OfType<TreeView<FileNode>>().Single();
            tree.GoTo(Workspace.Children(_directory, _directory).Single(node => node.Name == name));
        }

        internal void WaitForStatus(string status) => Assert.True(SpinWait.SpinUntil(() =>
        {
            Screen.ExecuteCommand("script-output");
            return Preview.Title.EndsWith(status, StringComparison.Ordinal);
        }, 5000), $"Expected {status}; got {Preview.Title}");

        public void Dispose()
        {
            Screen.Dispose();
            _parent.Dispose();
            _app.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                Skip = "Shell scripts require macOS or Linux.";
            }
        }
    }
}
