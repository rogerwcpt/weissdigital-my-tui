using MyTui.UI;
using Terminal.Gui.Input;

namespace MyTui.Tests;

public class CloseKeyTests
{
    [Fact]
    public void MacDeleteAndForwardDeleteClose()
    {
        Assert.True(SessionScreen.IsCloseKey(Key.Backspace));
        Assert.True(SessionScreen.IsCloseKey(Key.Delete));
        Assert.True(SessionScreen.IsCloseKey(Key.DeleteChar));
    }

    [Fact]
    public void ModifiedOrOtherKeysDoNotClose()
    {
        Assert.False(SessionScreen.IsCloseKey(Key.Backspace.WithCtrl));
        Assert.False(SessionScreen.IsCloseKey(Key.Delete.WithShift));
        Assert.False(SessionScreen.IsCloseKey(Key.Enter));
        Assert.False(SessionScreen.IsCloseKey(Key.D.WithCtrl));
    }
}
