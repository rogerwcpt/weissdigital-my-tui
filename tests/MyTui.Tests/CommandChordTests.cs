using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace MyTui.Tests;

[Collection("HostTheme")]
public class CommandChordTests
{
    [Theory]
    [InlineData("\u001b[107;9u")]
    [InlineData("\u001b[107:75:107;9;107u")]
    [InlineData("\u001b[107;9:1u")]
    [InlineData("\u001b[107;137u")]
    public void KittyCommandKKeepsTheSuperBit(string sequence)
    {
        Key key = CommandChord.Parse(sequence)!;
        Assert.Equal(ModifierKey.Super, key.ModifierKey);
        Assert.False(key.IsCtrl);
        Assert.False(key.IsShift);
        Assert.True(AppKeys.TryRoute(key, out AppCommand command));
        Assert.Equal(AppCommand.Menu, command);
    }

    [Theory]
    [InlineData("\u001b[107u")]
    [InlineData("\u001b[107;5u")]
    [InlineData("\u001b[107;10u")]
    [InlineData("\u001b[106;9u")]
    public void OtherKittyKeysDoNotBecomeCommandK(string sequence)
    {
        Key key = CommandChord.Parse(sequence)!;
        Assert.NotEqual(ModifierKey.Super, key.ModifierKey);
        Assert.False(AppKeys.IsCommandChord(key));
    }

    [Fact]
    public void InstalledParserReportsCommandK()
    {
        IApplication app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        try
        {
            Assert.True(CommandChord.Install(app));
            Assert.True(CommandChord.Install(app));
            var processor = (AnsiInputProcessor)app.Driver!.GetInputProcessor();
            Key? seen = null;
            processor.KeyDown += (_, key) => seen = key;
            foreach (char ch in "\u001b[107;9u")
            {
                processor.InputQueue.Enqueue(ch);
            }

            processor.ProcessQueue();
            Assert.NotNull(seen);
            Assert.True(AppKeys.IsCommandChord(seen!));
            Assert.True(AppKeys.TryRoute(seen, out AppCommand command));
            Assert.Equal(AppCommand.Menu, command);
        }
        finally
        {
            app.Dispose();
        }
    }
}
