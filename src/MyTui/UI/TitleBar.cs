using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// Burger on the left of the window title, then the app name. The burger opens the command palette.
/// </summary>
internal sealed class TitleBar : View
{
    internal const char Burger = '\u2261';

    private const string Label = "[\u2261] my-tui";

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
        if (column is 0 or 1 or 2)
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
    private readonly bool _boxed;

    private BorderGlyph(char glyph, Action click, bool atEnd, bool boxed)
    {
        _glyph = glyph;
        _click = click;
        _boxed = boxed;
        CanFocus = false;
        Y = 0;
        Width = boxed ? 3 : 1;
        Height = 1;
        X = atEnd ? Pos.AnchorEnd(boxed ? 4 : 2) : 1;
    }

    internal static BorderGlyph Place(FrameView frame, char glyph, bool atEnd, Action click, bool boxed = false)
    {
        var button = new BorderGlyph(glyph, click, atEnd, boxed);
        frame.Border.GetOrCreateView().Add(button);
        return button;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        if (!_boxed)
        {
            AddRune(0, 0, new Rune(_glyph));
            return true;
        }

        AddRune(0, 0, new Rune('['));
        AddRune(1, 0, new Rune(_glyph));
        AddRune(2, 0, new Rune(']'));
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
