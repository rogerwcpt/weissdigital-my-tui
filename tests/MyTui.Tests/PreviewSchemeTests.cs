using MyTui.UI;
using Terminal.Gui.Drawing;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace MyTui.Tests;

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
}
