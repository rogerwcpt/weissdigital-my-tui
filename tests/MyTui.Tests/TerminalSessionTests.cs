using MyTui.Terminal;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class TerminalSessionTests
{
    [UnixFact(Timeout = 15000)]
    public async Task SgrAttributes_SurviveInTheGrid()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "printf '\\033[1;31mRED\\033[0m \\033[1mBOLD\\033[0m \\033[3mITAL\\033[0m'"],
                80,
                24,
                environment: CleanEnv());
            ScreenFrame frame = WaitFor(session, value => value.Contains("RED") && value.Contains("BOLD") && value.Contains("ITAL"));
            PaintedCell red = Cell(frame, "R");
            PaintedCell bold = Cell(frame, "B");
            PaintedCell italic = Cell(frame, "I");
            Assert.True(red.Bold);
            Assert.True(red.ForegroundRed > red.ForegroundGreen);
            Assert.True(red.ForegroundRed > red.ForegroundBlue);
            Assert.True(bold.Bold);
            Assert.True(italic.Italic);
            Assert.False(frame.PlainText.Contains('\u001b'));
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
    public async Task Sessions_DoNotShareOutput_AndCloseIsLocal()
    {
        TerminalSession? first = null;
        TerminalSession? second = null;
        try
        {
            first = TerminalSession.Start("/bin/cat", ["/bin/cat"], 80, 24, environment: CleanEnv());
            second = TerminalSession.Start("/bin/cat", ["/bin/cat"], 80, 24, environment: CleanEnv());
            first.Write("AAA"u8);
            second.Write("BBB"u8);
            ScreenFrame left = WaitFor(first, value => value.Contains("AAA"));
            ScreenFrame right = WaitFor(second, value => value.Contains("BBB"));
            Assert.DoesNotContain("BBB", left.PlainText);
            Assert.DoesNotContain("AAA", right.PlainText);

            int firstPid = first.Pid;
            int secondPid = second.Pid;
            first.Dispose();
            first = null;
            Assert.False(PtySession.IsRunning(firstPid));
            Assert.True(PtySession.IsRunning(secondPid));

            second.Write("CCC"u8);
            ScreenFrame after = WaitFor(second, value => value.Contains("CCC"));
            Assert.Contains("BBB", after.PlainText);
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task Resize_TracksThePtyAndVt()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start("/bin/cat", ["/bin/cat"], 80, 24, environment: CleanEnv());
            for (int i = 0; i < 20; i++)
            {
                session.Resize(20 + i, 6 + (i % 5));
            }

            ScreenFrame frame = session.Capture();
            Assert.Equal(39, frame.Columns);
            Assert.Equal(10, frame.Rows);
            Assert.Equal(39 * 10, frame.Cells.Length);
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task Child_ObservesPtyResize()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start("/bin/sh", ["/bin/sh"], 80, 24, environment: CleanEnv());
            session.Write("stty size\n"u8);
            WaitFor(session, value => value.Contains("24 80"), 4000);
            session.Resize(40, 12);
            session.Resize(51, 13);
            session.Write("stty size\n"u8);
            ScreenFrame frame = WaitFor(session, value => value.Contains("13 51"), 4000);
            Assert.Equal(51, frame.Columns);
            Assert.Equal(13, frame.Rows);
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 20000)]
    public async Task Vim_IntroAndInsertStayOnTheirRows()
    {
        if (!File.Exists("/usr/bin/vim"))
        {
            return;
        }

        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/usr/bin/vim",
                ["/usr/bin/vim", "-Nu", "NONE", "-n", "-i", "NONE"],
                80,
                24,
                environment: CleanEnv());
            ScreenFrame intro = WaitFor(session, value => value.Contains("Vi IMproved"), 8000);
            Assert.Contains("Moolenaar et al.", intro.PlainText);
            Assert.Contains("Vi IMproved", intro.PlainText.Split('\n').First(line => line.Contains("Vi IMproved")));

            session.Write("ihello\x1b"u8);
            ScreenFrame inserted = WaitFor(session, value => value.Contains("hello"), 4000);
            Assert.Contains("hello", inserted.PlainText.Split('\n')[0].Trim());

            session.Write(":q!\r"u8);
            long deadline = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < deadline && !session.HasExited)
            {
                Thread.Sleep(20);
            }

            Assert.True(session.HasExited, session.Capture().PlainText.Replace('\n', '|'));
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 20000)]
    public async Task Pi_CtrlD_LeavesTheProcess()
    {
        const string pi = "/opt/homebrew/bin/pi";
        if (!File.Exists(pi))
        {
            return;
        }

        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                pi,
                [pi, "--no-session", "--offline"],
                100,
                32);
            WaitFor(session, value => value.Contains("clear/exit") || value.Contains("v1."), 12000);
            byte[]? encoded = session.Encode(Key.D.WithCtrl);
            string hex = encoded is null ? "null" : Convert.ToHexString(encoded);
            if (encoded is { Length: > 0 })
            {
                session.Write(encoded);
            }

            long deadline = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < deadline && !session.HasExited)
            {
                Thread.Sleep(20);
            }

            Assert.True(session.HasExited, hex + " " + session.Capture().PlainText.Replace('\n', '|'));
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task Scroll_MovesTheVtViewport()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "i=0; while [ \"$i\" -lt 40 ]; do printf 'L%02d\\n' \"$i\"; i=$((i+1)); done; sleep 20"],
                80,
                24,
                environment: CleanEnv());
            WaitFor(session, value => value.Contains("L39"));
            Assert.True(session.ScrollOffset > 0);
            session.ScrollToTop();
            ScreenFrame top = WaitFor(session, value => value.Contains("L00"));
            Assert.Equal(0, session.ScrollOffset);
            Assert.DoesNotContain("L39", top.PlainText);
            int moved = session.ScrollOffset;
            session.ScrollBy(4);
            Assert.Equal(moved + 4, session.ScrollOffset);
            session.ScrollToBottom();
            WaitFor(session, value => value.Contains("L39"));
            Assert.True(session.ScrollOffset > 0);
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 15000)]
    public async Task Paste_FollowsTheChildBracketedMode()
    {
        byte[] plain = PasteInput.Encode("hi\n"u8, false);
        Assert.Equal("hi\r"u8.ToArray(), plain);
        byte[] wrapped = PasteInput.Encode("hi\n"u8, true);
        Assert.Equal("\u001b[200~hi\n\u001b[201~"u8.ToArray(), wrapped);

        string directory = Directory.CreateTempSubdirectory("my-tui-paste").FullName;
        try
        {
            await ExpectPaste(directory, "plain", bracketed: false, "hi\r");
            await ExpectPaste(directory, "bracket", bracketed: true, "\u001b[200~hi\n\u001b[201~");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task ExpectPaste(string directory, string name, bool bracketed, string expected)
    {
        string file = Path.Combine(directory, name);
        string mode = bracketed ? "printf '\\033[?2004h'; " : "";
        string script = mode + "stty raw -echo; printf 'READY\\n'; dd bs=1 count=" + expected.Length + " of='" + file + "'";
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start("/bin/sh", ["/bin/sh", "-c", script], 80, 24, environment: CleanEnv());
            WaitFor(session, value => value.Contains("READY"));
            if (bracketed)
            {
                long deadline = Environment.TickCount64 + 2000;
                while (Environment.TickCount64 < deadline && !session.BracketedPaste)
                {
                    Thread.Sleep(20);
                }

                Assert.True(session.BracketedPaste);
            }

            session.Paste("hi\n");
            long fileDeadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < fileDeadline && !File.Exists(file))
            {
                Thread.Sleep(20);
            }

            Assert.True(File.Exists(file), session.Capture().PlainText);
            string body = "";
            long readDeadline = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < readDeadline)
            {
                body = File.ReadAllText(file);
                if (body == expected)
                {
                    break;
                }

                Thread.Sleep(20);
            }

            Assert.Equal(expected, body);
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    private static PaintedCell Cell(ScreenFrame frame, string grapheme) =>
        frame.Cells.First(cell => cell.Grapheme == grapheme);

    private static ScreenFrame WaitFor(TerminalSession session, Func<string, bool> ready, int timeoutMs = 3000)
    {
        ScreenFrame frame = session.Capture();
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !ready(frame.PlainText))
        {
            Thread.Sleep(20);
            frame = session.Capture();
        }

        Assert.True(ready(frame.PlainText), frame.PlainText);
        return frame;
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
