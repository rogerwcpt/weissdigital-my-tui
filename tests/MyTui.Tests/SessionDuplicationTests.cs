using MyTui.Sessions;

namespace MyTui.Tests;

public class SessionDuplicationTests
{
    [Fact]
    public void Duplicate_CopiesLaunchSettingsAndPersistsANewFocusedTerminalSession()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-duplicate").FullName;
        string path = Path.Combine(directory, "state.json");
        try
        {
            var manager = SessionManager.Load(path);
            Session source = manager.Create("agent", directory, "pi", ["--offline"],
                new Dictionary<string, string> { ["EXPLICIT"] = "value" });
            manager.SetTab(source, SessionTab.Files);
            source.SelectedPath = Path.Combine(directory, "notes.md");
            source.StartError = "Previous launch failed";

            Session copy = manager.Duplicate(source);

            Assert.Equal("agent-2", copy.Name);
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal(source.Cwd, copy.Cwd);
            Assert.Equal(source.Command, copy.Command);
            Assert.Equal(source.Args, copy.Args);
            Assert.Equal("value", copy.Env["EXPLICIT"]);
            Assert.NotSame(source.Args, copy.Args);
            Assert.NotSame(source.Env, copy.Env);
            Assert.Equal(SessionTab.Terminal, copy.Tab);
            Assert.Null(copy.SelectedPath);
            Assert.Null(copy.StartError);
            Assert.Null(copy.Terminal);
            Assert.Same(copy, manager.Focused);
            Assert.Equal(1, manager.FocusedIndex);

            var restored = SessionManager.Load(path);
            Assert.Equal(2, restored.Sessions.Count);
            Assert.Equal(copy.Id, restored.Focused!.Id);
            Assert.Equal("agent-2", restored.Focused.Name);
            Assert.Equal(directory, restored.Focused.Cwd);
            Assert.Equal("pi", restored.Focused.Command);
            Assert.Equal(["--offline"], restored.Focused.Args);
            Assert.Equal("value", restored.Focused.Env["EXPLICIT"]);
            Assert.Equal(SessionTab.Terminal, restored.Focused.Tab);
            Assert.Null(restored.Focused.Terminal);
            Assert.Equal(SessionTab.Files, restored.Sessions[0].Tab);

            copy.Args.Add("--verbose");
            copy.Env["EXPLICIT"] = "changed";
            Assert.Equal(["--offline"], source.Args);
            Assert.Equal("value", source.Env["EXPLICIT"]);
            Assert.Equal("agent", source.Name);
            Assert.Equal(SessionTab.Files, source.Tab);
            Assert.Equal("Previous launch failed", source.StartError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("project with spaces")]
    [InlineData("project-2")]
    public void Duplicate_AppendsACountAndSkipsExistingNames(string name)
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-duplicate-names").FullName;
        try
        {
            var manager = SessionManager.Load(Path.Combine(directory, "state.json"));
            Session source = manager.Create(name, directory, "/bin/sh", []);
            manager.Create(name + "-2", directory, "/bin/sh", []);
            manager.Create(name + "-4", directory, "/bin/sh", []);

            Assert.Equal(name + "-3", manager.Duplicate(source).Name);
            Assert.Equal(name + "-5", manager.Duplicate(source).Name);
            Assert.Equal(name, source.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [UnixFact(Timeout = 15000)]
    public async Task Duplicate_StartsAnIndependentProcessInTheSameFolder()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-duplicate-live").FullName;
        var manager = SessionManager.Load(Path.Combine(directory, "state.json"));
        Session source = manager.Create("shell", directory, "/bin/sh",
            ["-c", "pwd; printf '%s\\n' \"$DUPLICATE_TEST\"; exec /bin/cat"],
            new Dictionary<string, string> { ["DUPLICATE_TEST"] = "copied-environment" });
        Session? copy = null;
        try
        {
            source.Start(160, 24);
            copy = manager.Duplicate(source);
            Assert.Null(copy.Terminal);
            copy.Start(160, 24);

            Assert.True(source.IsRunning);
            Assert.True(copy.IsRunning);
            Assert.NotSame(source.Terminal, copy.Terminal);
            Assert.NotEqual(source.Terminal!.Pid, copy.Terminal!.Pid);
            string output = copy.Terminal.Capture().PlainText;
            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline
                && (!output.Contains(Path.GetFileName(directory)) || !output.Contains("copied-environment")))
            {
                await Task.Delay(20);
                output = copy.Terminal.Capture().PlainText;
            }

            Assert.Contains(Path.GetFileName(directory), output);
            Assert.Contains("copied-environment", output);
            copy.Stop();
            Assert.True(source.IsRunning);
        }
        finally
        {
            copy?.Stop();
            source.Stop();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                Skip = "The PTY host is macOS and Linux only.";
            }
            else if (!File.Exists("/bin/sh") || !File.Exists("/bin/cat"))
            {
                Skip = "Requires /bin/sh and /bin/cat.";
            }
        }
    }
}
