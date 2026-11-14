using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class AppKeysTests
{
    [Fact]
    public void CommanderAndClipboardChordsRun()
    {
        Assert.True(AppKeys.TryRoute(Key.F1, out AppCommand command));
        Assert.Equal(AppCommand.Sessions, command);
        Assert.True(AppKeys.TryRoute(Key.F3, out command));
        Assert.Equal(AppCommand.Preview, command);
        Assert.True(AppKeys.TryRoute(Key.F6, out command));
        Assert.Equal(AppCommand.Rename, command);
        Assert.True(AppKeys.TryRoute(Key.F7, out command));
        Assert.Equal(AppCommand.CreateFolder, command);
        Assert.True(AppKeys.TryRoute(Key.F8, out command));
        Assert.Equal(AppCommand.Delete, command);
        Assert.True(AppKeys.TryRoute(Key.F9, out command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.True(AppKeys.TryRoute(Key.F10, out command));
        Assert.Equal(AppCommand.Quit, command);
        Assert.True(AppKeys.TryRoute(Key.F12, out command));
        Assert.Equal(AppCommand.Center, command);
        Assert.True(AppKeys.TryRoute(new Key(Key.K) { ModifierKey = ModifierKey.Super }, out command));
        Assert.Equal(AppCommand.Menu, command);
        Assert.False(AppKeys.TryRoute(Key.K, out _));
        Assert.True(AppKeys.TryRoute(Key.C.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Copy, command);
        Assert.True(AppKeys.TryRoute(Key.V.WithCtrl.WithShift, out command));
        Assert.Equal(AppCommand.Paste, command);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(9, 8)]
    [InlineData(0, 9)]
    public void CtrlOptionDigitFocusesThatSession(int digit, int index)
    {
        Key key = digit switch
        {
            0 => Key.D0,
            1 => Key.D1,
            2 => Key.D2,
            _ => Key.D9,
        };
        Assert.True(AppKeys.TrySession(key.WithCtrl.WithAlt, out int found));
        Assert.Equal(index, found);
        Assert.False(AppKeys.TryRoute(key.WithCtrl.WithAlt, out _));
    }

    [Theory]
    [InlineData("\u001b[49;7u", 0)]
    [InlineData("\u001b[48;7u", 9)]
    public void KittyCtrlOptionDigitIsASessionChord(string sequence, int index)
    {
        Key key = CommandChord.Parse(sequence)!;
        Assert.True(AppKeys.TrySession(key, out int found));
        Assert.Equal(index, found);
    }

    [Theory]
    [MemberData(nameof(PlainDigits))]
    public void DigitsWithoutCtrlOptionStayWithTheProgram(Key key) =>
        Assert.False(AppKeys.TrySession(key, out _));

    public static TheoryData<Key> PlainDigits() =>
    [
        Key.D1, Key.D1.WithCtrl, Key.D1.WithShift, Key.D1.WithAlt,
        Key.D1.WithCtrl.WithShift, Key.D1.WithCtrl.WithShift.WithAlt, Key.D0.WithShift,
    ];

    [Theory]
    [MemberData(nameof(ModifiedFunctionKeys))]
    public void ModifiedFunctionKeysGoToTheProgram(Key key) => Assert.False(AppKeys.TryRoute(key, out _));

    public static TheoryData<Key> ModifiedFunctionKeys()
    {
        var keys = new TheoryData<Key>();
        foreach (Key key in new[] { Key.F1, Key.F3, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F12 })
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

    [Fact]
    public void SessionNumbersAreOneBasedAndSearchSkipsThem()
    {
        Assert.Equal("1  pi", SessionScreen.Numbered("pi", 0));
        Assert.Equal("10  pi", SessionScreen.Numbered("pi", 9));
        Assert.Equal("pi ●", SessionScreen.SearchText("1  pi ●"));
        Assert.Equal("pi", SessionScreen.SearchText("10  pi"));
        Assert.Equal("notes", SessionScreen.SearchText("notes"));
    }
}
