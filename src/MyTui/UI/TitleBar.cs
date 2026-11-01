using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// Burger and F1 on the left of the window title, then the app name. Both open the same menu.
/// </summary>
internal sealed class TitleBar : View
{
    internal const char Burger = '\u2261';

    private const string Label = "\u2261 F1  my-tui";

    private readonly Action _open;

    internal TitleBar(Action open)
    {
        _open = open;
        CanFocus = false;
        X = 1;
        Y = 0;
        Width = Label.Length;
        Height = 1;
    }

    internal static TitleBar Attach(Window window, Action open)
    {
        window.Border.Settings &= ~BorderSettings.Title;
        var bar = new TitleBar(open);
        window.Border.GetOrCreateView().Add(bar);
        return bar;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        int column = 0;
        foreach (Rune rune in Label.EnumerateRunes())
        {
            AddRune(column, 0, rune);
            column++;
        }

        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Position is not { } position || !mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            return false;
        }

        int column = (int)position.X;
        if (column is 0 or 2 or 3)
        {
            _open();
            return true;
        }

        return false;
    }
}

/// <summary>One glyph on a frame border. The end side sits just inside the corner.</summary>
internal sealed class BorderGlyph : View
{
    private readonly char _glyph;
    private readonly Action _click;

    private BorderGlyph(char glyph, Action click, bool atEnd)
    {
        _glyph = glyph;
        _click = click;
        CanFocus = false;
        Y = 0;
        Width = 1;
        Height = 1;
        X = atEnd ? Pos.AnchorEnd(2) : 1;
    }

    internal static BorderGlyph Place(FrameView frame, char glyph, bool atEnd, Action click)
    {
        var button = new BorderGlyph(glyph, click, atEnd);
        frame.Border.GetOrCreateView().Add(button);
        return button;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        AddRune(0, 0, new Rune(_glyph));
        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            return false;
        }

        _click();
        return true;
    }
}
