using Point = System.Drawing.Point;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace MyTui.Tests;

[Collection("HostTheme")]
public class PreviewSchemeTests
{
    [Fact]
    public void ReadOnlyAndCodeStayOnTheTerminalDefault()
    {
        HostTheme.Apply(ThemeNames.Terminal);
        Scheme scheme = HostTheme.Preview();
        var detected = new TuiAttribute(new Color(232, 232, 232), new Color(28, 28, 28));

        TuiAttribute readOnly = scheme.GetAttributeForRole(VisualRole.ReadOnly, detected);
        TuiAttribute code = scheme.GetAttributeForRole(VisualRole.Code, detected);
        TuiAttribute editable = scheme.GetAttributeForRole(VisualRole.Editable, detected);

        Assert.Equal(Color.None, readOnly.Foreground);
        Assert.Equal(Color.None, readOnly.Background);
        Assert.Equal(Color.None, editable.Foreground);
        Assert.Equal(Color.None, editable.Background);
        Assert.Equal(Color.None, code.Foreground);
        Assert.Equal(Color.None, code.Background);
    }

    [Theory]
    [InlineData(ThemeNames.Terminal, false)]
    [InlineData(ThemeNames.Graphite, false)]
    [InlineData("Green Phosphor", false)]
    [InlineData("Green Phosphor", true)]
    [InlineData("Dark", true)]
    public void SelectionIsDrawnInReverseVideoInEveryTheme(string theme, bool markdown)
    {
        HostTheme.Apply(theme);
        try
        {
            using IApplication app = Application.Create();
            app.Init(DriverRegistry.Names.ANSI);
            app.Driver!.SetScreenSize(40, 10);
            var window = new Window();
            View view = markdown
                ? new PreviewMarkdown { Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true, Text = "hello world" }
                : new PreviewTextView { Content = "hello world" };
            view.SetScheme(HostTheme.Preview());
            window.Add(view);
            SessionToken? token = app.Begin(window);
            app.LayoutAndDraw();
            Point origin = view.ViewportToScreen(new Point(0, 0));
            IInputInjector injector = app.GetInputInjector();
            foreach ((int x, MouseFlags flags) in new[]
            {
                (1, MouseFlags.LeftButtonPressed),
                (5, MouseFlags.LeftButtonPressed | MouseFlags.PositionReport),
                (5, MouseFlags.LeftButtonReleased),
            })
            {
                injector.InjectMouse(new Mouse { ScreenPosition = origin with { X = origin.X + x }, Flags = flags });
            }

            app.LayoutAndDraw(true);
            Cell[,] cells = app.Driver.Contents!;
            TuiAttribute? plain = cells[origin.Y, origin.X].Attribute;
            TuiAttribute? selected = cells[origin.Y, origin.X + 2].Attribute;
            Assert.True(selected!.Value.Style.HasFlag(TextStyle.Reverse), $"{theme}: {selected}");
            Assert.False(plain!.Value.Style.HasFlag(TextStyle.Reverse), $"{theme}: {plain}");
            app.End(token!);
        }
        finally
        {
            HostTheme.Apply(ThemeNames.Terminal);
        }
    }
}
