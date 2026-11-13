using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class AppKeysTests
{
    [Fact]
    public void CommanderAndClipboardChordsRun()
    {
        Assert.True(AppKeys.TryRoute(Key.F3, out AppCommand command));
        Assert.Equal(AppCommand.Preview, command);
        Assert.True(AppKeys.TryRoute(Key.F6, out command));
        Assert.Equal(AppCommand.Rename, command);
        Assert.True(AppKeys.TryRoute(Key.F8, out command));
        Assert.Equal(AppCommand.Delete, command);
        Assert.True(AppKeys.TryRoute(Key.F9, out command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.True(AppKeys.TryRoute(Key.F10, out command));
        Assert.Equal(AppCommand.Quit, command);
        Assert.True(AppKeys.TryRoute(Key.C.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Copy, command);
        Assert.True(AppKeys.TryRoute(Key.V.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Paste, command);
    }

    [Theory]
    [MemberData(nameof(ModifiedFunctionKeys))]
    public void ModifiedFunctionKeysGoToTheProgram(Key key) => Assert.False(AppKeys.TryRoute(key, out _));

    public static TheoryData<Key> ModifiedFunctionKeys()
    {
        var keys = new TheoryData<Key>();
        foreach (Key key in new[] { Key.F3, Key.F6, Key.F8, Key.F9, Key.F10 })
        {
            keys.Add(key.WithCtrl);
            keys.Add(key.WithShift);
            keys.Add(key.WithAlt);
            keys.Add(key.WithCtrl.WithShift);
            keys.Add(key.WithCtrl.WithAlt);
            keys.Add(key.WithShift.WithAlt);
            keys.Add(key.WithCtrl.WithShift.WithAlt);
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(RemovedKeys))]
    public void RemovedShortcutsGoToTheProgram(Key key) => Assert.False(AppKeys.TryRoute(key, out _));

    public static TheoryData<Key> RemovedKeys() =>
    [
        Key.B.WithCtrl, Key.N.WithCtrl, Key.O.WithCtrl, Key.Q.WithCtrl, Key.Tab.WithCtrl,
        Key.P.WithCtrl.WithShift, Key.F2, Key.F4, Key.F11, Key.Delete,
    ];
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
