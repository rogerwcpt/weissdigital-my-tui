using System.Text;
using System.Text.RegularExpressions;

namespace MyTui.Terminal;

/// <summary>Two ends of a viewport selection. Either end may be the anchor.</summary>
internal readonly record struct CellRange(int Column1, int Row1, int Column2, int Row2)
{
    internal (int Column, int Row, int EndColumn, int EndRow) Ordered()
    {
        if (Row1 < Row2 || (Row1 == Row2 && Column1 <= Column2))
        {
            return (Column1, Row1, Column2, Row2);
        }

        return (Column2, Row2, Column1, Row1);
    }

    internal bool Contains(int column, int row)
    {
        (int startColumn, int startRow, int endColumn, int endRow) = Ordered();
        if (row < startRow || row > endRow)
        {
            return false;
        }

        if (row == startRow && column < startColumn)
        {
            return false;
        }

        if (row == endRow && column > endColumn)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Selection and URL text for one viewport. A cell is one grapheme. "" is a wide-character tail.
/// A blank cell is a single space. <paramref name="wrapped"/> is true when that row continues on the next.
/// </summary>
internal static partial class GridText
{
    internal static string Copy(
        IReadOnlyList<string[]> rows,
        IReadOnlyList<bool> wrapped,
        int column1,
        int row1,
        int column2,
        int row2)
    {
        if (rows.Count == 0)
        {
            return "";
        }

        var range = new CellRange(column1, row1, column2, row2);
        (int startColumn, int startRow, int endColumn, int endRow) = range.Ordered();
        startRow = Math.Clamp(startRow, 0, rows.Count - 1);
        endRow = Math.Clamp(endRow, 0, rows.Count - 1);
        var text = new StringBuilder();
        for (int row = startRow; row <= endRow; row++)
        {
            string[] line = rows[row];
            int from = row == startRow ? Math.Clamp(startColumn, 0, Math.Max(0, line.Length - 1)) : 0;
            int to = row == endRow ? Math.Clamp(endColumn, 0, Math.Max(0, line.Length - 1)) : Math.Max(0, line.Length - 1);
            if (from > 0 && from < line.Length && line[from].Length == 0)
            {
                from--;
            }

            if (row > startRow && (row - 1 >= wrapped.Count || !wrapped[row - 1]))
            {
                text.Append('\n');
            }

            var piece = new StringBuilder();
            for (int column = from; column <= to && column < line.Length; column++)
            {
                if (line[column].Length == 0)
                {
                    continue;
                }

                piece.Append(line[column]);
            }

            text.Append(piece.ToString().TrimEnd());
        }

        return text.ToString().TrimEnd();
    }

    internal static bool TryWord(
        IReadOnlyList<string[]> rows,
        IReadOnlyList<bool> wrapped,
        int column,
        int row,
        out CellRange range)
    {
        range = default;
        if (!TryHit(rows, column, row, out int hitColumn, out _))
        {
            return false;
        }

        if (!IsWord(rows[row][hitColumn]))
        {
            return false;
        }

        int startRow = row;
        int startColumn = hitColumn;
        while (startColumn > 0 && IsWord(rows[startRow][startColumn - 1]))
        {
            startColumn--;
        }

        while (startColumn == 0 && startRow > 0 && Wrapped(wrapped, startRow - 1) && LastWord(rows[startRow - 1]) is int previous)
        {
            startRow--;
            startColumn = previous;
            while (startColumn > 0 && IsWord(rows[startRow][startColumn - 1]))
            {
                startColumn--;
            }
        }

        int endRow = row;
        int endColumn = hitColumn;
        while (endColumn + 1 < rows[endRow].Length && IsWord(rows[endRow][endColumn + 1]))
        {
            endColumn++;
        }

        while (ReachesEnd(rows[endRow], endColumn) && Wrapped(wrapped, endRow) && endRow + 1 < rows.Count && IsWord(rows[endRow + 1][0]))
        {
            endRow++;
            endColumn = 0;
            while (endColumn + 1 < rows[endRow].Length && IsWord(rows[endRow][endColumn + 1]))
            {
                endColumn++;
            }
        }

        range = new CellRange(startColumn, startRow, endColumn, endRow);
        return true;
    }

    internal static bool TryLine(
        IReadOnlyList<string[]> rows,
        IReadOnlyList<bool> wrapped,
        int row,
        out CellRange range)
    {
        range = default;
        if (row < 0 || row >= rows.Count)
        {
            return false;
        }

        int first = row;
        while (first > 0 && Wrapped(wrapped, first - 1))
        {
            first--;
        }

        int last = row;
        while (Wrapped(wrapped, last) && last + 1 < rows.Count)
        {
            last++;
        }

        int end = rows[last].Length - 1;
        while (end > 0 && IsBlank(rows[last][end]))
        {
            end--;
        }

        if (end < 0 || IsBlank(rows[last][end]))
        {
            bool any = false;
            for (int line = first; line <= last && !any; line++)
            {
                any = rows[line].Any(cell => !IsBlank(cell));
            }

            if (!any)
            {
                return false;
            }
        }

        range = new CellRange(0, first, Math.Max(0, end), last);
        return true;
    }

    internal static string? UrlAt(IReadOnlyList<string[]> rows, IReadOnlyList<bool> wrapped, int column, int row)
    {
        if (!TryHit(rows, column, row, out int hitColumn, out int hitRow))
        {
            return null;
        }

        int first = hitRow;
        while (first > 0 && Wrapped(wrapped, first - 1))
        {
            first--;
        }

        int last = hitRow;
        while (Wrapped(wrapped, last) && last + 1 < rows.Count)
        {
            last++;
        }

        var text = new StringBuilder();
        var marks = new List<(int Row, int Column)>();
        for (int line = first; line <= last; line++)
        {
            for (int cell = 0; cell < rows[line].Length; cell++)
            {
                string grapheme = rows[line][cell];
                if (grapheme.Length == 0)
                {
                    continue;
                }

                for (int i = 0; i < grapheme.Length; i++)
                {
                    marks.Add((line, cell));
                }

                text.Append(grapheme);
            }
        }

        string lineText = text.ToString();
        foreach (Match match in Urls().Matches(lineText))
        {
            bool hit = false;
            int end = match.Index + match.Length;
            for (int i = match.Index; i < end && i < marks.Count; i++)
            {
                if (marks[i].Row == hitRow && marks[i].Column == hitColumn)
                {
                    hit = true;
                    break;
                }
            }

            if (!hit)
            {
                continue;
            }

            string url = TrimUrl(match.Value);
            return IsOpenable(url) ? url : null;
        }

        return null;
    }

    internal static bool IsOpenable(string url)
    {
        if (url.Length is 0 or > 2048)
        {
            return false;
        }

        foreach (char character in url)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
            && (uri.Host.Length == 0 || uri.IsLoopback);
    }

    internal static string TrimUrl(string url)
    {
        int end = url.Length;
        while (end > 0 && IsTrailing(url, end))
        {
            end--;
        }

        return url[..end];
    }

    [GeneratedRegex("""(?:https?://|file://)[^\s<>"'`]+""", RegexOptions.CultureInvariant)]
    private static partial Regex Urls();

    private static bool TryHit(IReadOnlyList<string[]> rows, int column, int row, out int hitColumn, out int hitRow)
    {
        hitColumn = 0;
        hitRow = row;
        if (row < 0 || row >= rows.Count || column < 0)
        {
            return false;
        }

        string[] line = rows[row];
        if (line.Length == 0)
        {
            return false;
        }

        hitColumn = Math.Min(column, line.Length - 1);
        while (hitColumn > 0 && line[hitColumn].Length == 0)
        {
            hitColumn--;
        }

        return true;
    }

    private static bool IsWord(string grapheme) => grapheme.Length > 0 && !grapheme.All(char.IsWhiteSpace);

    private static bool IsBlank(string grapheme) => grapheme.Length == 0 || grapheme.All(char.IsWhiteSpace);

    private static bool Wrapped(IReadOnlyList<bool> wrapped, int row) => row >= 0 && row < wrapped.Count && wrapped[row];

    private static int? LastWord(string[] row)
    {
        for (int column = row.Length - 1; column >= 0; column--)
        {
            if (row[column].Length == 0)
            {
                continue;
            }

            return IsWord(row[column]) ? column : null;
        }

        return null;
    }

    private static bool ReachesEnd(string[] row, int column)
    {
        for (int next = column + 1; next < row.Length; next++)
        {
            if (row[next].Length == 0)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsTrailing(string url, int end)
    {
        char character = url[end - 1];
        if (character is '.' or ',' or ';' or ':' or '!' or '?' or ']' or '}' or '\'' or '"')
        {
            return true;
        }

        if (character != ')')
        {
            return false;
        }

        int open = 0;
        int close = 0;
        for (int i = 0; i < end; i++)
        {
            if (url[i] == '(')
            {
                open++;
            }
            else if (url[i] == ')')
            {
                close++;
            }
        }

        return close > open;
    }
}
