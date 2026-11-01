using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class AppKeysTests
{
    [Fact]
    public void LeaderArmsAndTheNextKeyRuns()
    {
        var leader = new LeaderState();
        KeyRoute armed = AppKeys.Route(AppKeys.Leader, inTerminal: true, leader, now: 0, out _);
        Assert.Equal(KeyRoute.Consumed, armed);
        Assert.True(leader.IsArmed(0));

        KeyRoute ran = AppKeys.Route(Key.N, inTerminal: true, leader, now: 10, out AppCommand command);
        Assert.Equal(KeyRoute.Run, ran);
        Assert.Equal(AppCommand.NewSession, command);
        Assert.False(leader.IsArmed(10));
    }

    [Fact]
    public void SecondLeaderIsForwarded()
    {
        var leader = new LeaderState();
        AppKeys.Route(AppKeys.Leader, true, leader, 0, out _);
        KeyRoute route = AppKeys.Route(AppKeys.Leader, true, leader, 10, out _);
        Assert.Equal(KeyRoute.Forward, route);
        Assert.False(leader.IsArmed(10));
    }

    [Fact]
    public void LeaderTimesOutAndTheKeyIsHandledOnItsOwn()
    {
        var leader = new LeaderState();
        AppKeys.Route(AppKeys.Leader, true, leader, 0, out _);
        long later = LeaderState.TimeoutMilliseconds + 1;
        KeyRoute route = AppKeys.Route(Key.R.WithCtrl, true, leader, later, out _);
        Assert.Equal(KeyRoute.Forward, route);
        Assert.False(leader.IsArmed(later));
    }

    [Fact]
    public void EscapeAndUnboundKeysCancelTheLeader()
    {
        var leader = new LeaderState();
        AppKeys.Route(AppKeys.Leader, true, leader, 0, out _);
        Assert.Equal(KeyRoute.Consumed, AppKeys.Route(Key.Esc, true, leader, 10, out _));

        AppKeys.Route(AppKeys.Leader, true, leader, 20, out _);
        Assert.Equal(KeyRoute.Consumed, AppKeys.Route(Key.A, true, leader, 30, out _));
        Assert.False(leader.IsArmed(30));
    }

    [Fact]
    public void DirectKeysPassThroughALiveTerminalAndRunOutsideIt()
    {
        var leader = new LeaderState();
        Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.R.WithCtrl, true, leader, 0, out _));
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.R.WithCtrl, false, leader, 0, out AppCommand command));
        Assert.Equal(AppCommand.Refresh, command);
        Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.Q.WithCtrl, true, leader, 0, out _));
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.Q.WithCtrl, false, leader, 0, out command));
        Assert.Equal(AppCommand.Quit, command);
    }

    [Fact]
    public void ModifiersHaveToMatch()
    {
        var leader = new LeaderState();
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.Tab.WithCtrl, false, leader, 0, out AppCommand command));
        Assert.Equal(AppCommand.NextPanel, command);
        Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.Tab.WithCtrl.WithAlt, false, leader, 0, out _));
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.F4, false, leader, 0, out command));
        Assert.Equal(AppCommand.HideSessions, command);
        Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.F4.WithCtrl, false, leader, 0, out _));
    }

    [Fact]
    public void CopyAndPasteWorkInsideTheTerminal()
    {
        var leader = new LeaderState();
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.C.WithCtrl.WithShift, true, leader, 0, out AppCommand command));
        Assert.Equal(AppCommand.Copy, command);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.V.WithCtrl.WithShift, true, leader, 0, out command));
        Assert.Equal(AppCommand.Paste, command);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.P.WithCtrl.WithShift, true, leader, 0, out command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.F9, true, leader, 0, out command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.F1, true, leader, 0, out _));
    }

    [Fact]
    public void QuestionMarkAndShiftedLettersMatchByCharacter()
    {
        var leader = new LeaderState();
        AppKeys.Route(AppKeys.Leader, true, leader, 0, out _);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(new Key('?'), true, leader, 10, out AppCommand command));
        Assert.Equal(AppCommand.Help, command);

        AppKeys.Route(AppKeys.Leader, true, leader, 12, out _);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(new Key('/').WithShift, true, leader, 14, out command));
        Assert.Equal(AppCommand.Help, command);

        AppKeys.Route(AppKeys.Leader, true, leader, 20, out _);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.T.WithShift, true, leader, 30, out command));
        Assert.Equal(AppCommand.Templates, command);

        AppKeys.Route(AppKeys.Leader, true, leader, 40, out _);
        Assert.Equal(KeyRoute.Run, AppKeys.Route(Key.T, true, leader, 50, out command));
        Assert.Equal(AppCommand.CycleTab, command);
    }

    [Fact]
    public void EveryCommandHasOneRowAndADistinctLeaderKey()
    {
        Assert.Equal(Enum.GetValues<AppCommand>().Length, AppKeys.All.Length);
        var seen = new HashSet<string>();
        IReadOnlyList<string> help = AppKeys.HelpLines();
        foreach (AppBinding binding in AppKeys.All)
        {
            Assert.False(string.IsNullOrEmpty(binding.AfterLeaderText));
            Assert.Contains(help, line => line.Contains(binding.Label, StringComparison.Ordinal));
            string identity = binding.AfterTab ? "tab" : binding.AfterShiftTab ? "shift-tab" : binding.AfterChar.ToString()!;
            Assert.True(seen.Add(identity));
        }
    }

    [Fact]
    public void LeaderTextParsesCtrlLetterSpaceAndBackslash()
    {
        Assert.True(AppKeys.TryParseLeader("ctrl+b", out Key key, out string label));
        Assert.Equal(Key.B.WithCtrl, key);
        Assert.Equal("Ctrl+B", label);
        Assert.True(AppKeys.TryParseLeader("ctrl+backslash", out key, out label));
        Assert.Equal(new Key('\\').WithCtrl, key);
        Assert.Equal("Ctrl+\\", label);
        Assert.False(AppKeys.TryParseLeader("alt+b", out _, out _));
    }

    [Fact]
    public void LeaderCanBeCtrlSpace()
    {
        Key previous = AppKeys.Leader;
        string previousText = AppKeys.LeaderText;
        try
        {
            Assert.True(AppKeys.TryParseLeader("ctrl+space", out Key key, out string label));
            AppKeys.SetLeader(key, label);
            var leader = new LeaderState();
            Assert.Equal(KeyRoute.Consumed, AppKeys.Route(Key.Space.WithCtrl, true, leader, 0, out _));
            Assert.Equal(KeyRoute.Forward, AppKeys.Route(Key.Space.WithCtrl, true, leader, 10, out _));
            Assert.Contains("Ctrl+Space", AppKeys.LeaderHint, StringComparison.Ordinal);
        }
        finally
        {
            AppKeys.SetLeader(previous, previousText);
        }
    }
}

public class StatusRowTests
{
    [Fact]
    public void HintSitsOnTheRightEdge()
    {
        string row = SessionScreen.StatusRow("shell  /tmp  pid 9", "F1 menu", 40);
        Assert.Equal(40, row.Length);
        Assert.EndsWith("F1 menu", row);
        Assert.StartsWith("shell  /tmp  pid 9", row);
    }

    [Fact]
    public void LongDetailsAreCut()
    {
        string row = SessionScreen.StatusRow(new string('a', 40), "F1 menu", 20);
        Assert.Equal(20, row.Length);
        Assert.EndsWith("F1 menu", row);
        Assert.Contains('…', row);
    }

    [Fact]
    public void NarrowRowsDropTheHint()
    {
        string row = SessionScreen.StatusRow("shell  /tmp  pid 9", "F1 menu", 18);
        Assert.Equal("shell  /tmp  pid 9", row);
    }

    [Fact]
    public void ActivityMarksPreferTheBell()
    {
        Assert.Equal("pi ●", SessionScreen.ActivityLabel("pi", unseen: true, bell: false));
        Assert.Equal("pi !", SessionScreen.ActivityLabel("pi", unseen: true, bell: true));
        Assert.Equal("pi", SessionScreen.ActivityLabel("pi", unseen: false, bell: false));
    }
}
