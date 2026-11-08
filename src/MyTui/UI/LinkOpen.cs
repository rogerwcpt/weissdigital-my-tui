using System.Diagnostics;
using MyTui.Terminal;

namespace MyTui.UI;

internal static class LinkOpen
{
    internal static bool TryOpen(string url)
    {
        if (!GridText.IsOpenable(url))
        {
            return false;
        }

        return TryLaunch(url);
    }

    internal static bool TryOpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return TryLaunch(Path.GetFullPath(path));
    }

    internal static ProcessStartInfo? StartInfo(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(target) { UseShellExecute = true };
        }

        string? program = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsLinux() ? "xdg-open" : null;
        return program is null ? null : new ProcessStartInfo
        {
            FileName = program,
            ArgumentList = { target },
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static bool TryLaunch(string target)
    {
        try
        {
            ProcessStartInfo? info = StartInfo(target);
            if (info is null)
            {
                return false;
            }

            using Process? process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
