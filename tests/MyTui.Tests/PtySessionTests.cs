using System.Runtime.InteropServices;
using System.Text;
using MyTui.Terminal;

namespace MyTui.Tests;

[Collection("PTY signals")]
public class PtySessionTests
{
    [UnixFact(Timeout = 15000)]
    public async Task CtrlC_InterruptsTheChildEvenWhenTheHostIgnoresSigint()
    {
        PtySession? session = null;
        nint previous = SetSignal(2, 1);
        Assert.NotEqual((nint)(-1), previous);
        try
        {
            try
            {
                session = PtySession.Start(
                    "/bin/sh",
                    ["/bin/sh", "-c", "trap 'exit 42' INT; printf 'READY\\n'; while :; do sleep 1; done"],
                    80, 24, CleanEnv());
            }
            finally
            {
                SetSignal(2, previous);
            }

            Capture(session, value => value.Contains("READY"), TimeSpan.FromSeconds(3));
            session.Write([0x03]);
            Assert.True(WaitUntil(session.PollExited, TimeSpan.FromSeconds(3)));
            Assert.Equal(42, session.ExitCode);
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [DllImport("libc", EntryPoint = "signal")]
    private static extern nint SetSignal(int signal, nint handler);

    [UnixFact(Timeout = 15000)]
    public async Task Kill_StopsTheForegroundJobOfAnInteractiveShell()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-kill").FullName;
        string pidFile = Path.Combine(directory, "child-pid");
        PtySession? session = null;
        int child = 0;
        try
        {
            session = PtySession.Start("/bin/sh", ["/bin/sh", "-i"], 80, 24, CleanEnv());
            session.StartReading();
            session.Write(Encoding.UTF8.GetBytes(
                $"/bin/sh -c 'trap \"\" TERM; echo $$ > \"$1\"; exec cat' sh '{pidFile}'\n"));
            Assert.True(WaitUntil(
                () => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0,
                TimeSpan.FromSeconds(3)));
            child = int.Parse(File.ReadAllText(pidFile).Trim());
            Assert.True(PtySession.IsRunning(child));
            int shell = session.Pid;

            session.Kill();
            session.Dispose();

            Assert.False(PtySession.IsRunning(shell));
            Assert.True(WaitUntil(() => !PtySession.IsRunning(child), TimeSpan.FromSeconds(3)));
        }
        finally
        {
            if (child > 1 && PtySession.IsRunning(child))
            {
                PtyNative.Kill(child, PtyNative.SigKill);
            }

            session?.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task InteractiveShellIsBusyOnlyWhileACommandRuns()
    {
        PtySession? session = null;
        try
        {
            session = PtySession.Start("/bin/sh", ["/bin/sh", "-i"], 80, 24, CleanEnv());
            session.StartReading();
            Assert.True(WaitUntil(() => !session.IsBusy, TimeSpan.FromSeconds(3)));
            session.Write("sleep 2\n"u8);
            Assert.True(WaitUntil(() => session.IsBusy, TimeSpan.FromSeconds(3)));
            Assert.True(WaitUntil(() => !session.IsBusy, TimeSpan.FromSeconds(4)));
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task EchoesTypedBytes()
    {
        PtySession? session = null;
        try
        {
            session = PtySession.Start("/bin/cat", ["/bin/cat"], 80, 24, CleanEnv());
            string text = Capture(session, value => value.Contains('Z'), TimeSpan.FromSeconds(3), () => session.Write("Z"u8));
            Assert.Contains('Z', text);
        }
        finally
        {
            int pid = session?.Pid ?? 0;
            session?.Dispose();
            if (pid > 0)
            {
                Assert.False(PtySession.IsRunning(pid));
            }
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task ReapsTheProcessGroup()
    {
        PtySession? session = null;
        int shell = 0;
        int sleep = 0;
        try
        {
            session = PtySession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "sleep 60 & echo $!; wait"],
                80,
                24,
                CleanEnv());
            shell = session.Pid;
            string text = Capture(session, value => value.Contains('\n'), TimeSpan.FromSeconds(3));
            sleep = int.Parse(text.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim());
        }
        finally
        {
            session?.Dispose();
        }

        Assert.False(PtySession.IsRunning(shell));
        Assert.False(PtySession.IsRunning(sleep));
        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task ChildStartsInTheRequestedDirectory()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-cwd").FullName;
        PtySession? session = null;
        try
        {
            session = PtySession.Start("/bin/pwd", ["/bin/pwd"], 80, 24, CleanEnv(), directory);
            string text = Capture(session, value => value.Contains(Path.GetFileName(directory)), TimeSpan.FromSeconds(3));
            Assert.Contains(Path.GetFileName(directory), text);
        }
        finally
        {
            session?.Dispose();
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task ExitCodeIsTheChildStatus()
    {
        PtySession? session = null;
        try
        {
            session = PtySession.Start("/bin/sh", ["/bin/sh", "-c", "exit 7"], 80, 24, CleanEnv());
            bool exited = WaitUntil(() => session.PollExited() && session.ExitCode == 7, TimeSpan.FromSeconds(3));
            Assert.True(exited, $"exit {session.ExitCode}");
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    private static string Capture(PtySession session, Func<string, bool> ready, TimeSpan timeout, Action? afterStart = null)
    {
        var got = new StringBuilder();
        session.Output += bytes =>
        {
            lock (got)
            {
                got.Append(Encoding.UTF8.GetString(bytes));
            }
        };
        session.StartReading();
        afterStart?.Invoke();
        bool ok = WaitUntil(() =>
        {
            lock (got)
            {
                return ready(got.ToString());
            }
        }, timeout);
        string text;
        lock (got)
        {
            text = got.ToString();
        }

        Assert.True(ok, text);
        return text;
    }

    private static bool WaitUntil(Func<bool> ready, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (ready())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return ready();
    }

    private static Dictionary<string, string> CleanEnv() => new()
    {
        ["PATH"] = "/bin:/usr/bin",
        ["HOME"] = "/tmp",
        ["TERM"] = "xterm-256color",
    };

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

[CollectionDefinition("PTY signals", DisableParallelization = true)]
public class PtySignalCollection
{
}
