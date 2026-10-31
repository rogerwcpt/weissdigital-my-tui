using System.Text;
using MyTui.Terminal;

namespace MyTui.Tests;

public class PtySessionTests
{
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
