using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TuiAttribute = global::Terminal.Gui.Drawing.Attribute;

namespace MyTui.UI;

/// <summary>
/// The chrome stays on Terminal.Gui's host scheme. <c>NO_COLOR</c> and <c>TERM=dumb</c> drop color.
/// </summary>
internal static class HostTheme
{
    internal static bool ColorEnabled =>
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    internal static void Apply()
    {
        // Dialog, menu, and error views ship with a blue scheme. The window already uses the
        // terminal's own colors, so these do too: Color.None, with reverse for the focused control.
        Scheme terminal = Plain();
        SchemeManager.AddScheme("Dialog", terminal);
        SchemeManager.AddScheme("Menu", terminal);
        SchemeManager.AddScheme("Error", terminal);
        if (!ColorEnabled)
        {
            SchemeManager.AddScheme("Base", terminal);
        }
    }

    internal static Scheme Plain()
    {
        var plain = new TuiAttribute(Color.None, Color.None);
        var reverse = new TuiAttribute(Color.None, Color.None, TextStyle.Reverse);
        return new Scheme
        {
            Normal = plain,
            HotNormal = plain,
            Focus = reverse,
            HotFocus = reverse,
            Active = reverse,
            HotActive = reverse,
            Highlight = reverse,
            Editable = plain,
            ReadOnly = plain,
            Disabled = plain,
        };
    }

    /// <summary>
    /// Preview colors stay on the terminal default. Leaving read-only and code roles unset lets Terminal.Gui
    /// dim them into a gray field once it has learned the host palette.
    /// </summary>
    internal static Scheme Preview()
    {
        var plain = new TuiAttribute(Color.None, Color.None);
        var reverse = new TuiAttribute(Color.None, Color.None, TextStyle.Reverse);
        return new Scheme
        {
            Normal = plain,
            HotNormal = plain,
            Focus = reverse,
            HotFocus = reverse,
            Active = reverse,
            HotActive = reverse,
            Highlight = reverse,
            Editable = plain,
            ReadOnly = plain,
            Disabled = plain,
            Code = plain,
            CodeComment = plain,
            CodeKeyword = plain,
            CodeString = plain,
            CodeNumber = plain,
            CodeOperator = plain,
            CodeType = plain,
            CodePreprocessor = plain,
            CodeIdentifier = plain,
            CodeConstant = plain,
            CodePunctuation = plain,
            CodeFunctionName = plain,
            CodeAttribute = plain,
        };
    }
}
