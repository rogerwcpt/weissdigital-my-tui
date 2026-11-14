using System.Diagnostics;
using MyTui.Sessions;

namespace MyTui.Tests;

public class ScriptRunTests
{
    [UnixFact]
    public async Task HonorsShebangAndCapturesBothStreamsExitCodeCwdAndEnvironment()
    {
        using var workspace = new TestDirectory();
        string path = workspace.Script("script with 'quotes'; and spaces.sh", """
            #!/bin/bash
            values=(one two)
            printf 'bash:%s\n' "${values[1]}"
            printf 'cwd:%s\n' "$PWD"
            printf 'env:%s\n' "$SCRIPT_TEST"
            printf 'error-output\n' >&2
            exit 7
            """);
        // No chmod: the interpreter can read a non-executable script.
        using var run = ScriptRun.Start(workspace.Path, path, new Dictionary<string, string> { ["SCRIPT_TEST"] = "copied" });
        await run.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        ScriptRun.Snapshot result = run.Capture();
        Assert.False(run.IsRunning);
        Assert.Equal("exited 7", result.Status);
        Assert.Contains("bash:two", result.Text);
        string cwdLine = Assert.Single(result.Text.Split('\n'), line => line.StartsWith("cwd:", StringComparison.Ordinal));
        Assert.EndsWith(System.IO.Path.GetFileName(workspace.Path), cwdLine); // macOS may expand /var to /private/var.
        Assert.Contains("env:copied", result.Text);
        Assert.Contains("error-output", result.Text);
        Assert.Contains("[exited 7]", result.Text);
    }

    [UnixFact]
    public async Task StreamsPartialLinesBeforeExitAndClosesStdin()
    {
        using var workspace = new TestDirectory();
        string path = workspace.Script("live.sh", """
            if read answer; then exit 9; fi
            printf 'partial-output'
            while [ ! -f release ]; do sleep 0.02; done
            printf ' finished\n'
            """);
        using var run = ScriptRun.Start(workspace.Path, path);
        await Until(() => run.Capture().Text.Contains("partial-output"));
        int version = run.Version;
        Assert.True(run.IsRunning);
        Assert.False(run.Completion.IsCompleted);
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "release"), "");
        await run.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(run.Version > version);
        Assert.Contains("partial-output finished", run.Capture().Text);
        Assert.Equal("exited 0", run.Capture().Status);
    }

    [UnixFact]
    public async Task CapsOutputButContinuesDrainingBothPipes()
    {
        using var workspace = new TestDirectory();
        string path = workspace.Script("large.sh", """
            i=0
            while [ "$i" -lt 10000 ]; do
                printf 'a long line of script standard output\n'
                printf 'a long line of script standard error\n' >&2
                i=$((i + 1))
            done
            """);
        using var run = ScriptRun.Start(workspace.Path, path);
        await run.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        string text = run.Capture().Text;
        Assert.Contains("[output truncated]", text);
        Assert.Contains("[exited 0]", text);
        Assert.InRange(text.Length, ScriptRun.MaxOutputChars, ScriptRun.MaxOutputChars + 1024);
    }

    [UnixFact]
    public async Task StopKillsTheInterpreterAndItsChildAndDrainsPendingOutput()
    {
        using var workspace = new TestDirectory();
        string path = workspace.Script("stop.sh", """
            sleep 60 &
            printf '%s' "$!" > child.pid
            printf 'stdout-ready\n'
            printf 'stderr-ready\n' >&2
            touch ready
            wait
            """);
        using var run = ScriptRun.Start(workspace.Path, path);
        Assert.True(SpinWait.SpinUntil(() => File.Exists(System.IO.Path.Combine(workspace.Path, "ready")), 5000));
        int child = int.Parse(File.ReadAllText(System.IO.Path.Combine(workspace.Path, "child.pid")));
        Assert.True(IsRunning(child));
        run.Stop();
        run.Stop(); // Idempotent, including once completed.
        await run.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => !IsRunning(child));
        Assert.False(run.IsRunning);
        Assert.Equal("stopped", run.Capture().Status);
        Assert.Contains("stdout-ready", run.Capture().Text);
        Assert.Contains("stderr-ready", run.Capture().Text);
    }

    [UnixFact]
    public void RejectsNonScriptsMissingFilesAndPathsOutsideTheWorkspace()
    {
        using var workspace = new TestDirectory();
        using var outside = new TestDirectory();
        string notScript = workspace.Script("notes.txt", "echo no");
        string other = outside.Script("outside.sh", "echo no");
        string link = System.IO.Path.Combine(workspace.Path, "link.sh");
        File.CreateSymbolicLink(link, other);
        Assert.Throws<IOException>(() => ScriptRun.Start(workspace.Path, notScript));
        Assert.Throws<IOException>(() => ScriptRun.Start(workspace.Path, System.IO.Path.Combine(workspace.Path, "missing.sh")));
        Assert.Throws<IOException>(() => ScriptRun.Start(workspace.Path, other));
        Assert.Throws<IOException>(() => ScriptRun.Start(workspace.Path, link));
    }

    [UnixFact]
    public async Task SessionRunsAreIsolatedNotPersistedAndStoppedWhenClosed()
    {
        using var workspace = new TestDirectory();
        string path = workspace.Script("session.sh", "printf 'ready'; sleep 60");
        string state = System.IO.Path.Combine(workspace.Path, "state.json");
        var manager = SessionManager.Load(state);
        Session first = manager.Create("first", workspace.Path, "/bin/sh", []);
        Session second = manager.Create("second", workspace.Path, "/bin/sh", []);
        first.RunScript(path);
        second.RunScript(path);
        using ScriptRun firstRun = first.Script!;
        using ScriptRun secondRun = second.Script!;
        await Until(() => firstRun.Capture().Text.Contains("ready") && secondRun.Capture().Text.Contains("ready"));
        Assert.Null(first.Terminal);
        Assert.Null(second.Terminal);
        Assert.Throws<InvalidOperationException>(() => first.RunScript(path));
        Assert.Same(firstRun, first.Script);
        Assert.All(SessionManager.Load(state).Sessions, session =>
        {
            Assert.Null(session.Script);
            Assert.False(session.ShowingScriptOutput);
        });
        Assert.Null(manager.Duplicate(first).Script);
        manager.Close(first);
        await firstRun.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(firstRun.IsRunning);
        Assert.True(secondRun.IsRunning);
        second.StopScript();
        await secondRun.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        // Finished runs can be replaced without affecting the terminal settings.
        string quick = workspace.Script("quick.sh", "echo done");
        second.RunScript(quick);
        using ScriptRun replacement = second.Script!;
        await replacement.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotSame(secondRun, replacement);
        Assert.True(second.ShowingScriptOutput);
        Assert.Equal("/bin/sh", second.Command);
    }

    private static async Task Until(Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private sealed class TestDirectory : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("my-tui-script").FullName;
        internal string Script(string name, string content)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
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
