using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// Maximize and hide controls plus the frame title, drawn on the left of the top border.
/// The built-in title starts at the same cells, so it is turned off and this view draws the name.
/// </summary>
internal sealed class PanelCaption : View
{
    internal const char MaximizeGlyph = '\u25A1';
    internal const char RestoreGlyph = '\u2750';
    internal const char MinimizeGlyph = '\u2212';

    private readonly FrameView _frame;
    private readonly Action _maximize;
    private readonly Action _minimize;
    private bool _maximized;

    private PanelCaption(FrameView frame, Action maximize, Action minimize)
    {
        _frame = frame;
        _maximize = maximize;
        _minimize = minimize;
        CanFocus = false;
        X = 1;
        Y = 0;
        Height = 1;
        Width = Dim.Func(_ => WidthFor(frame));
        frame.TitleChanged += (_, _) =>
        {
            frame.Border.Settings &= ~BorderSettings.Title;
            SetNeedsLayout();
            SetNeedsDraw();
        };
    }

    internal static PanelCaption Attach(FrameView frame, Action maximize, Action minimize)
    {
        frame.Border.Settings &= ~BorderSettings.Title;
        var caption = new PanelCaption(frame, maximize, minimize);
        frame.Border.GetOrCreateView().Add(caption);
        return caption;
    }

    internal bool Maximized
    {
        get => _maximized;
        set
        {
            if (_maximized == value)
            {
                return;
            }

            _maximized = value;
            SetNeedsDraw();
        }
    }

    internal char Glyph => _maximized ? RestoreGlyph : MaximizeGlyph;

    internal void Activate(int column)
    {
        if (column == 0)
        {
            _maximize();
        }
        else if (column == 1)
        {
            _minimize();
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        if (width < 1)
        {
            return true;
        }

        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        AddRune(0, 0, new Rune(Glyph));
        if (width > 1)
        {
            AddRune(1, 0, new Rune(MinimizeGlyph));
        }

        int column = 2;
        string title = _frame.Title ?? "";
        if (title.Length > 0 && width > 2)
        {
            AddRune(2, 0, new Rune(' '));
            column = 3;
            foreach (Rune rune in title.EnumerateRunes())
            {
                if (column >= width)
                {
                    break;
                }

                AddRune(column, 0, rune);
                column++;
            }
        }

        while (column < width)
        {
            AddRune(column, 0, new Rune(' '));
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
        if (column is not (0 or 1))
        {
            return false;
        }

        Activate(column);
        return true;
    }

    private static int WidthFor(FrameView frame)
    {
        int text = 0;
        foreach (Rune _ in (frame.Title ?? "").EnumerateRunes())
        {
            text++;
        }

        int wanted = text == 0 ? 2 : text + 3;
        int room = frame.Frame.Width > 2 ? frame.Frame.Width - 2 : wanted;
        return Math.Max(2, Math.Min(wanted, room));
    }
}
