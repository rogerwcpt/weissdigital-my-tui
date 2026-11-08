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
    public async Task AnsiColors_UseTheVsCodePaletteByDefault()
    {
        TerminalPalette.Load("/nonexistent/config.json");
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "printf '\\033[31mR\\033[94mB\\033[0m'"],
                80,
                24,
                environment: CleanEnv());
            ScreenFrame frame = WaitFor(session, value => value.Contains("RB"));
            PaintedCell red = Cell(frame, "R");
            PaintedCell blue = Cell(frame, "B");
            Assert.Equal((0xCD, 0x31, 0x31), ((int)red.ForegroundRed, (int)red.ForegroundGreen, (int)red.ForegroundBlue));
            Assert.Equal((0x3B, 0x8E, 0xEA), ((int)blue.ForegroundRed, (int)blue.ForegroundGreen, (int)blue.ForegroundBlue));
        }
        finally
        {
            session?.Dispose();
        }

        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("""{"terminalPalette":"xterm"}""", 4, 0x00, 0x00, 0xEE)]
    [InlineData("""{"terminalPalette":"Tomorrow-Night"}""", 1, 0xCC, 0x66, 0x66)]
    [InlineData("""{"terminalPalette":["#000000","#FF8800"]}""", 1, 0xFF, 0x88, 0x00)]
    [InlineData("""{"terminalPalette":["#000000","#FF8800"]}""", 4, 0x24, 0x72, 0xC8)]
    [InlineData("""{"terminalPalette":"nope"}""", 1, 0xCD, 0x31, 0x31)]
    [InlineData("""{"terminalPalette":["#00000"]}""", 1, 0xCD, 0x31, 0x31)]
    public void TerminalPalette_ReadsConfig(string json, int index, int r, int g, int b)
    {
        string path = Path.Combine(Path.GetTempPath(), $"my-tui-palette-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            TerminalPalette.Load(path);
            Ghostty.Vt.Types.ColorRgb color = TerminalPalette.Apply(new Ghostty.Vt.Types.ColorRgb[256])[index];
            Assert.Equal((r, g, b), ((int)color.R, (int)color.G, (int)color.B));
        }
        finally
        {
            File.Delete(path);
            TerminalPalette.Load(path);
        }
    }

    [UnixFact(Timeout = 15000)]
    public async Task UrlAt_ReadsOsc8PlainTextAndAWrappedUrl()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "printf '\\033]8;;https://example.com/osc\\033\\\\docs\\033]8;;\\033\\\\ see https://example.com/plain.\\n'"],
                80,
                12,
                environment: CleanEnv());
            ScreenFrame frame = WaitFor(session, value => value.Contains("docs") && value.Contains("plain"));
            Assert.Equal("https://example.com/osc", session.UrlAt(0, 0));
            Assert.Equal("https://example.com/plain", session.UrlAt(9, 0));
            Assert.Null(session.UrlAt(5, 0));
            Assert.Equal("docs", session.CopyRange(0, 0, 3, 0));
            Assert.True(Cell(frame, "d").Underline);
        }
        finally
        {
            session?.Dispose();
        }

        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "printf 'https://example.com/wrap\\n'"],
                10,
                8,
                environment: CleanEnv());
            WaitFor(session, value => value.Contains("wrap"));
            Assert.Equal("https://example.com/wrap", session.UrlAt(0, 2));
        }
        finally
        {
            session?.Dispose();
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

    [UnixFact(Timeout = 15000)]
    public async Task Pointer_UsesSgrAndSkipsMotionTheProgramDidNotAskFor()
    {
        TerminalSession? session = null;
        try
        {
            session = TerminalSession.Start(
                "/bin/sh",
                ["/bin/sh", "-c", "printf '\\033[?1000h\\033[?1006h\\n'; sleep 2"],
                80,
                24,
                environment: CleanEnv());
            long deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline && !session.TracksMouse)
            {
                Thread.Sleep(20);
            }

            Assert.True(session.TracksMouse);
            Assert.Equal(
                "\u001b[<0;1;5M"u8.ToArray(),
                session.EncodePointer(0, 4, PointerKind.Press, 0, ctrl: false, alt: false));
            Assert.Null(session.EncodePointer(1, 4, PointerKind.Motion, 0, ctrl: false, alt: false));
            Assert.Equal(
                "\u001b[<0;2;5m"u8.ToArray(),
                session.EncodePointer(1, 4, PointerKind.Release, 0, ctrl: false, alt: false));
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
