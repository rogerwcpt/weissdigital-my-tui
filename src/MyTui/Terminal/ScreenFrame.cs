namespace MyTui.Terminal;

/// <summary>
/// One visible cell after color and style resolution. The grid draws this and does not touch the VT.
/// </summary>
internal readonly record struct PaintedCell(
    string Grapheme,
    byte ForegroundRed,
    byte ForegroundGreen,
    byte ForegroundBlue,
    byte BackgroundRed,
    byte BackgroundGreen,
    byte BackgroundBlue,
    bool Bold,
    bool Italic,
    bool Faint,
    bool Underline,
    bool WideTail)
{
    internal static PaintedCell Blank { get; } = new(
        "",
        229, 229, 229,
        0, 0, 0,
        false, false, false, false, false);
}

/// <summary>A copy of the visible VT grid.</summary>
internal sealed class ScreenFrame
{
    internal required int Columns { get; init; }

    internal required int Rows { get; init; }

    internal required PaintedCell[] Cells { get; init; }

    internal required string PlainText { get; init; }

    internal required int CursorX { get; init; }

    internal required int CursorY { get; init; }

    internal required bool CursorVisible { get; init; }

    internal required bool CursorBlinking { get; init; }

    internal PaintedCell Cell(int column, int row)
    {
        if (column < 0 || row < 0 || column >= Columns || row >= Rows)
        {
            return PaintedCell.Blank;
        }

        return Cells[(row * Columns) + column];
    }
}
