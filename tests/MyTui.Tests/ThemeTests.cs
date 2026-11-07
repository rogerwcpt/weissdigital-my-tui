using System.Text.Json;
using MyTui.UI;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.Tests;

[Collection("HostTheme")]
public class ThemeTests
{
    [Fact]
    public void TerminalIsTheDefaultAndAnUnknownNameReturnsToIt()
    {
        HostTheme.Apply(null);
        Assert.Equal(ThemeNames.Terminal, HostTheme.Current);
        Assert.Equal(Color.None, HostTheme.Scheme().Normal.Foreground);
        Assert.Equal(Color.None, HostTheme.Preview().Code.Foreground);

        HostTheme.Apply("nope");
        Assert.Equal(ThemeNames.Terminal, HostTheme.Current);
        Assert.Equal(Color.None, HostTheme.Scheme().Normal.Background);
    }

    [Fact]
    public void GraphiteAndPaperPaintTheirOwnInk()
    {
        Scheme graphite = HostTheme.Palette(ThemeNames.Graphite);
        Scheme paper = HostTheme.Palette(ThemeNames.Paper);
        Assert.Equal(201, graphite.Normal.Foreground.R);
        Assert.Equal(204, graphite.Normal.Foreground.G);
        Assert.Equal(209, graphite.Normal.Foreground.B);
        Assert.Equal(79, graphite.Focus.Background.R);
        Assert.Equal(31, paper.Normal.Foreground.R);
        Assert.Equal(35, paper.Normal.Foreground.G);
        Assert.Equal(40, paper.Normal.Foreground.B);
        Assert.NotEqual(graphite.Normal.Foreground, paper.Normal.Foreground);
    }

    [Fact]
    public void NoColorKeepsTheNameAndPaintsTheTerminal()
    {
        HostTheme.Apply(ThemeNames.Graphite);
        if (HostTheme.ColorEnabled)
        {
            Assert.Equal(201, HostTheme.Scheme().Normal.Foreground.R);
            Assert.Equal(201, HostTheme.Preview().Code.Foreground.R);
        }
        else
        {
            Assert.Equal(ThemeNames.Graphite, HostTheme.Current);
            Assert.Equal(Color.None, HostTheme.Scheme().Normal.Foreground);
            Assert.Equal(Color.None, HostTheme.Preview().Code.Foreground);
        }

        HostTheme.Apply(ThemeNames.Terminal);
    }

    [Fact]
    public void SavingAThemeKeepsTheLeader()
    {
        string directory = Path.Combine(Path.GetTempPath(), "mytui-theme-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "config.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """{ "leader": "ctrl+a" }""");
            HostTheme.Apply(ThemeNames.Graphite);
            HostTheme.Save(path);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("ctrl+a", document.RootElement.GetProperty("leader").GetString());
            Assert.Equal(ThemeNames.Graphite, document.RootElement.GetProperty("theme").GetString());

            HostTheme.Apply(ThemeNames.Terminal);
            HostTheme.Load(path);
            Assert.Equal(ThemeNames.Graphite, HostTheme.Current);
        }
        finally
        {
            HostTheme.Apply(ThemeNames.Terminal);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplyingASchemePaintsAnExistingBorderButtonAndDoesNotCreateAnotherBorder()
    {
        var frame = new FrameView { Title = "Sessions", Width = 28, Height = 8 };
        var button = new Label { Text = "x", Width = 1, Height = 1 };
        frame.Border.GetOrCreateView().Add(button);
        var plain = new View { Width = 10, Height = 2 };
        Scheme scheme = HostTheme.Palette(ThemeNames.Graphite);

        SessionScreen.ApplyScheme(frame, scheme);
        SessionScreen.ApplyScheme(plain, scheme);

        Assert.Same(scheme, button.GetScheme());
        Assert.Same(scheme, frame.Border.View!.GetScheme());
        Assert.Null(plain.Border.View);
        Assert.Null(frame.Border.View.Border.View);
    }
}
