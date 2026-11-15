using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>Markdown preview that reports a finished mouse selection, so releasing a drag copies it like the terminal does.</summary>
internal sealed class PreviewMarkdown : Markdown
{
    /// <summary>Blank cells between the text and the frame, and between the text and the scrollbar.</summary>
    internal const int Inset = 1;

    internal PreviewMarkdown()
    {
        // The built-in bar sits in the innermost right-padding column and adds one when it appears.
        // Anchoring it to the outer edge leaves the column we set here between the text and the bar.
        Padding.Thickness = new Thickness(Inset, Inset, Inset, Inset);
        VerticalScrollBar.X = Pos.AnchorEnd();
    }

    /// <summary>Called with the text when a drag, double-click, or triple-click ends with a selection.</summary>
    internal Action<string>? Selected { get; set; }

    /// <summary>
    /// The copy mark is drawn in the code block, and that view copies without raising an event.
    /// The same cells the block treats as the mark: the last two columns of its first row.
    /// </summary>
    internal static bool IsCodeCopyClick(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
            || mouse.View is not MarkdownCodeBlock block
            || !block.ShowCopyButton
            || block.Viewport.Width < 2)
        {
            return false;
        }

        Point local = block.ScreenToViewport(mouse.ScreenPosition);
        int column = block.Viewport.Width - 2;
        return local.Y == 0 && local.X >= column && local.X <= column + 1;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        bool handled = base.OnMouseEvent(mouse);
        bool finished = mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased)
            || mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)
            || mouse.Flags.HasFlag(MouseFlags.LeftButtonTripleClicked);
        if (finished && SelectedText is { Length: > 0 } text)
        {
            Selected?.Invoke(text);
        }

        return handled;
    }

    /// <summary>Enter copies and clears a selection. Without one it still opens the focused link.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Enter && SelectedText is { Length: > 0 } text)
        {
            Selected?.Invoke(text);
            ClearSelection();
            SetNeedsDraw();
            return true;
        }

        return base.OnKeyDown(key);
    }
}
