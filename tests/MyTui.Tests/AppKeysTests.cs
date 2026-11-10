using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class AppKeysTests
{
    [Fact]
    public void OnlyF9AndClipboardChordsRun()
    {
        Assert.True(AppKeys.TryRoute(Key.F9, out AppCommand command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.True(AppKeys.TryRoute(Key.C.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Copy, command);
        Assert.True(AppKeys.TryRoute(Key.V.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Paste, command);
    }

    [Theory]
    [MemberData(nameof(RemovedKeys))]
    public void RemovedShortcutsGoToTheProgram(Key key) => Assert.False(AppKeys.TryRoute(key, out _));

    public static TheoryData<Key> RemovedKeys() =>
    [
        Key.B.WithCtrl, Key.N.WithCtrl, Key.O.WithCtrl, Key.Q.WithCtrl, Key.Tab.WithCtrl,
        Key.P.WithCtrl.WithShift, Key.F2, Key.F4, Key.F6, Key.F11, Key.Delete, Key.F9.WithCtrl,
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
