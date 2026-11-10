using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>Markdown preview that reports a finished mouse selection, so releasing a drag copies it like the terminal does.</summary>
internal sealed class PreviewMarkdown : Markdown
{
    /// <summary>Called with the text when a drag, double-click, or triple-click ends with a selection.</summary>
    internal Action<string>? Selected { get; set; }

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
