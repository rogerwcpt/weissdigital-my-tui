using System.Reflection;
using MyTui.Sessions;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class RunSelectionTests
{
    [Fact]
    public void AnEmptySelectionAsksForTextAndDoesNotStartACopy()
    {
        using var workspace = new TestWorkspace("echo hello");
        workspace.Screen.ExecuteCommand("run-selection");

        Assert.Equal("Select text to run", workspace.Footer);
        Assert.Equal(1, workspace.Sessions.Source!.Count);
        Assert.Contains("· stopped", LabelAt(workspace.Sessions, 0));
        Assert.False(workspace.CopyToast.Visible);
        Assert.Equal(" Copied ", workspace.CopyToast.Text.ToString());
    }

    [UnixFact]
    public void SelectedPreviewTextRunsInADuplicateSession()
    {
        using var workspace = new TestWorkspace("echo hello | /usr/bin/tr a-z A-Z");
        workspace.Screen.ExecuteCommand("files");
        workspace.Select("command.sh");
        workspace.Text.SelectAll();

        workspace.Screen.ExecuteCommand("run-selection");

        Assert.Equal("Running in test-2", workspace.Footer);
        Assert.Equal(2, workspace.Sessions.Source!.Count);
        Assert.Contains("test · stopped", LabelAt(workspace.Sessions, 0));
        Assert.StartsWith("2  test-2", LabelAt(workspace.Sessions, 1));
        Assert.NotNull(workspace.Grid.Session);
        Assert.True(SpinWait.SpinUntil(
            () => workspace.Grid.Session!.Capture().PlainText.Contains("HELLO"),
            8000));
    }

    private static string LabelAt(ListView list, int index)
    {
        object source = list.Source ?? throw new InvalidOperationException("Session list has no source.");
        System.Collections.IEnumerable? items = source.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.GetValue(source))
            .OfType<System.Collections.IEnumerable>()
            .FirstOrDefault(value => value is not string);
        Assert.NotNull(items);
        return items.Cast<object>().ElementAt(index).ToString() ?? "";
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-run-selection").FullName;
        private readonly IApplication _app = Application.Create();
        private readonly View _parent;

        internal TestWorkspace(string command)
        {
            File.WriteAllText(Path.Combine(_directory, "command.sh"), command + "\n");
            string state = Path.Combine(_directory, "state.json");
            SessionManager.Load(state).Create("test", _directory, "/bin/sh", []);
            Screen = new SessionScreen(_app);
            _parent = new View { App = _app, Width = 100, Height = 30, CanFocus = true };
            _parent.Add(Screen.Sessions, Screen.Content);
            _parent.Layout();
            Screen.Start(state);
        }

        internal SessionScreen Screen { get; }
        internal ListView Sessions => Screen.Sessions.SubViews.OfType<ListView>().Single();
        internal string Footer => Screen.Sessions.SubViews.OfType<Label>().Single().Text.ToString()!;
        internal Label CopyToast => Preview.SubViews.OfType<Label>().Single(label => label.Text == " Copied ");
        internal FrameView Preview => Screen.Content.SubViews.OfType<FrameView>().Single();
        internal PreviewTextView Text => Preview.SubViews.OfType<PreviewTextView>().Single();
        internal CellGridView Grid => Screen.Content.SubViews.OfType<Tabs>().Single()
            .SubViews.SelectMany(view => view.SubViews).OfType<CellGridView>().Single();

        internal void Select(string name)
        {
            TreeView<FileNode> tree = Screen.Content.SubViews.OfType<Tabs>().Single()
                .SubViews.SelectMany(view => view.SubViews).OfType<TreeView<FileNode>>().Single();
            tree.GoTo(Workspace.Children(_directory, _directory).Single(node => node.Name == name));
        }

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
                Skip = "Running a selection needs macOS or Linux.";
            }
            else if (!File.Exists("/bin/sh") || !File.Exists("/usr/bin/tr"))
            {
                Skip = "Requires /bin/sh and /usr/bin/tr.";
            }
        }
    }
}
