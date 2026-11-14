using MyTui.Sessions;
using MyTui.UI;

namespace MyTui.Tests;

public class SessionBusyTests
{
    [Theory]
    [InlineData("/bin/zsh", true, false, false)]
    [InlineData("bash", true, false, false)]
    [InlineData("fish", true, true, true)]
    [InlineData("pi", true, false, true)]
    [InlineData("/opt/homebrew/bin/pi", true, false, true)]
    [InlineData("zsh", false, true, false)]
    public void BusyDependsOnTheCommandAndWhetherWorkIsRunning(string command, bool running, bool foreground, bool busy)
    {
        Assert.Equal(busy, Session.IsBusySession(command, [], running, foreground));
    }

    [Fact]
    public void ShellCommandFlagIsTheRunningCommand()
    {
        Assert.False(Session.IsBusySession("zsh", ["-l"], running: true, foregroundWork: false));
        Assert.True(Session.IsBusySession("zsh", ["-lc", "make"], running: true, foregroundWork: false));
        Assert.True(Session.IsBusySession("/bin/bash", ["-c", "make"], running: true, foregroundWork: false));
    }

    [Fact]
    public void QuitNamesTheSessionsThatAreExecuting()
    {
        Assert.Equal(
            "1 session is still running. Quit and stop it?",
            CloseDialog.QuitMessage(1, ""));
        Assert.Equal(
            "1 session is still running. Quit and stop it?\nnotes is executing a command.",
            CloseDialog.QuitMessage(1, CloseDialog.BusyQuitText(["notes"])));
        Assert.Equal(
            "2 sessions are still running. Quit and stop them?\nnotes and build are executing a command.",
            CloseDialog.QuitMessage(2, CloseDialog.BusyQuitText(["notes", "build"])));
        Assert.Equal(
            "alpha, beta, gamma, and 1 more are executing a command.",
            CloseDialog.BusyQuitText(["alpha", "beta", "gamma", "delta"]));
    }
}
