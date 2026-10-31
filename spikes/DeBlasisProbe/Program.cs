using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ghostty.Vt;

const string Expected = "plain BOLD";
const string ExpectedBold = "0000001111";

Console.WriteLine("binding: DeBlasis.GhosttyVt");
Console.WriteLine("package-pin: 1.4.1");
Console.WriteLine("assembly: " + typeof(Terminal).Assembly.GetName().Version);
Console.WriteLine("runtime: " + RuntimeInformation.RuntimeIdentifier);

try
{
    using var terminal = new Terminal(80, 24);
    terminal.VTWrite("plain \u001b[1mBOLD\u001b[0m");
    using var state = new RenderState();
    state.Update(terminal);

    var text = new StringBuilder();
    var bold = new StringBuilder();
    foreach (var row in state.Rows)
    {
        foreach (var cell in row.Cells)
        {
            if (string.IsNullOrEmpty(cell.Grapheme))
            {
                if (text.Length > 0)
                {
                    break;
                }

                continue;
            }

            text.Append(cell.Grapheme);
            bold.Append(cell.Style.Bold ? '1' : '0');
            if (text.Length >= Expected.Length)
            {
                break;
            }
        }

        break;
    }

    Console.WriteLine("text: '" + text + "'");
    Console.WriteLine("bold: " + bold);
    bool pass = text.ToString() == Expected && bold.ToString() == ExpectedBold;
    Console.WriteLine(pass ? "result: PASS" : "result: FAIL");
    return pass ? 0 : 1;
}
catch (Exception ex)
{
    Console.WriteLine("result: LOAD-FAIL");
    Console.WriteLine(ex);
    return 2;
}
