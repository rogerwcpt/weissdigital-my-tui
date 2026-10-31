using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LibGhostty;

Console.WriteLine("binding: LibGhostty.Net");
Console.WriteLine("package-pin: 1.0.2");
Console.WriteLine("assembly: " + typeof(GhosttyNativeLibrary).Assembly.GetName().Version);
Console.WriteLine("runtime: " + RuntimeInformation.RuntimeIdentifier);

int vt = ProbeVt();
int pty = await ProbePty();
return vt != 0 ? vt : pty;

static int ProbeVt()
{
    const string Expected = "plain BOLD";
    const string ExpectedBold = "0000001111";
    GhosttyNativeLibrary? library = null;
    nint terminal = 0;
    nint state = 0;
    nint iterator = 0;
    nint cells = 0;
    try
    {
        library = GhosttyNativeLibrary.LoadFromPackage();
        Console.WriteLine("library: " + library.LibraryPath);
        int status = library.TerminalNew(
            new GhosttyNativeLibrary.TerminalOptions
            {
                Columns = 80,
                Rows = 24,
                MaxScrollback = 1000,
            },
            out terminal);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: LOAD-FAIL terminal-new {status}");
            return 2;
        }

        byte[] fixture = Encoding.UTF8.GetBytes("plain \u001b[1mBOLD\u001b[0m");
        GCHandle pin = GCHandle.Alloc(fixture, GCHandleType.Pinned);
        try
        {
            library.TerminalWrite(terminal, pin.AddrOfPinnedObject(), (nuint)fixture.Length);
        }
        finally
        {
            pin.Free();
        }

        status = library.RenderStateNew(out state);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: LOAD-FAIL render-state-new {status}");
            return 2;
        }

        status = library.RenderStateUpdate(state, terminal);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: LOAD-FAIL render-state-update {status}");
            return 2;
        }

        status = library.RowIteratorNew(out iterator);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: LOAD-FAIL row-iterator-new {status}");
            return 2;
        }

        status = library.RenderStateGetHandle(state, GhosttyNativeLibrary.RenderStateDataRowIterator, ref iterator);
        if (status != GhosttyNativeLibrary.Success || !library.RowIteratorNext(iterator))
        {
            Console.WriteLine($"result: FAIL row-iterator {status}");
            return 1;
        }

        status = library.RowCellsNew(out cells);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: LOAD-FAIL row-cells-new {status}");
            return 2;
        }

        status = library.RowGetCells(iterator, ref cells);
        if (status != GhosttyNativeLibrary.Success)
        {
            Console.WriteLine($"result: FAIL row-cells {status}");
            return 1;
        }

        nint loaded = NativeLibrary.Load(library.LibraryPath);
        var cellGet = Marshal.GetDelegateForFunctionPointer<CellGet>(
            NativeLibrary.GetExport(loaded, "ghostty_cell_get"));
        status = library.RenderStateGetU16(state, GhosttyNativeLibrary.RenderStateDataCols, out ushort columns);
        if (status != GhosttyNativeLibrary.Success || columns == 0)
        {
            Console.WriteLine($"result: FAIL columns {status} {columns}");
            return 1;
        }

        var text = new StringBuilder();
        var bold = new StringBuilder();
        for (ushort column = 0; column < columns && text.Length < Expected.Length; column++)
        {
            status = library.RowCellsSelect(cells, column);
            if (status != GhosttyNativeLibrary.Success)
            {
                Console.WriteLine($"result: FAIL select {column} {status}");
                return 1;
            }

            string grapheme = ReadGrapheme(library, cells, cellGet);
            if (grapheme.Length == 0)
            {
                if (text.Length > 0)
                {
                    break;
                }

                continue;
            }

            status = library.RowCellsGetStyle(cells, out GhosttyNativeLibrary.NativeStyle style);
            text.Append(grapheme);
            bold.Append(status == GhosttyNativeLibrary.Success && style.Bold != 0 ? '1' : '0');
        }

        Console.WriteLine("text: '" + text + "'");
        Console.WriteLine("bold: " + bold);
        bool pass = text.ToString() == Expected && bold.ToString() == ExpectedBold;
        Console.WriteLine(pass ? "vt-result: PASS" : "vt-result: FAIL");
        return pass ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine("vt-result: LOAD-FAIL");
        Console.WriteLine(ex);
        return 2;
    }
    finally
    {
        if (library is not null)
        {
            if (cells != 0)
            {
                library.RowCellsFree(cells);
            }

            if (iterator != 0)
            {
                library.RowIteratorFree(iterator);
            }

            if (state != 0)
            {
                library.RenderStateFree(state);
            }

            if (terminal != 0)
            {
                library.TerminalFree(terminal);
            }

            library.Dispose();
        }
    }
}

static string ReadGrapheme(GhosttyNativeLibrary library, nint cells, CellGet cellGet)
{
    int status = library.RowCellsGetU32(
        cells,
        GhosttyNativeLibrary.RenderStateRowCellsDataGraphemesLength,
        out uint length);
    if (status == GhosttyNativeLibrary.Success && length is > 0 and < 64)
    {
        var codepoints = new uint[length];
        GCHandle pin = GCHandle.Alloc(codepoints, GCHandleType.Pinned);
        try
        {
            status = library.RowCellsGetBuffer(
                cells,
                GhosttyNativeLibrary.RenderStateRowCellsDataGraphemesBuffer,
                pin.AddrOfPinnedObject());
        }
        finally
        {
            pin.Free();
        }

        if (status == GhosttyNativeLibrary.Success)
        {
            string combined = FromCodepoints(codepoints);
            if (combined.Length > 0)
            {
                return combined;
            }
        }
    }

    if (library.RowCellsGetRaw(cells, out ulong raw) != GhosttyNativeLibrary.Success)
    {
        return "";
    }

    if (cellGet(raw, 4, out uint hasText) != GhosttyNativeLibrary.Success || hasText == 0)
    {
        return "";
    }

    if (cellGet(raw, 1, out uint codepoint) != GhosttyNativeLibrary.Success || codepoint == 0)
    {
        return "";
    }

    return codepoint <= 0xFFFF
        ? ((char)codepoint).ToString()
        : char.ConvertFromUtf32((int)codepoint);
}

static string FromCodepoints(uint[] codepoints)
{
    var text = new StringBuilder();
    foreach (uint codepoint in codepoints)
    {
        if (codepoint == 0)
        {
            continue;
        }

        text.Append(codepoint <= 0xFFFF
            ? ((char)codepoint).ToString()
            : char.ConvertFromUtf32((int)codepoint));
    }

    return text.ToString();
}

static async Task<int> ProbePty()
{
    try
    {
        var options = new GhosttyPtyOptions
        {
            Name = "probe",
            Cols = 80,
            Rows = 24,
            Cwd = Environment.CurrentDirectory,
            App = "/bin/sh",
            CommandLine = ["-c", "printf 'pty-ok\\n'"],
            Environment = new Dictionary<string, string>(),
        };
        using IGhosttyPtyConnection connection = await GhosttyPtyConnectionFactory.StartAsync(options);
        using var buffer = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await connection.ReaderStream.CopyToAsync(buffer, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("pty-note: read timed out");
        }

        try
        {
            connection.Kill();
        }
        catch (Exception ex)
        {
            Console.WriteLine("pty-kill: " + ex.Message);
        }

        string text = Encoding.UTF8.GetString(buffer.ToArray());
        Console.WriteLine("pty-text: " + text.Replace("\n", "\\n", StringComparison.Ordinal));
        bool pass = text.Contains("pty-ok", StringComparison.Ordinal);
        Console.WriteLine(pass ? "pty-result: PASS" : "pty-result: FAIL");
        return pass ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine("pty-result: LOAD-FAIL");
        Console.WriteLine(ex);
        return 2;
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int CellGet(ulong rawCell, int data, out uint value);
