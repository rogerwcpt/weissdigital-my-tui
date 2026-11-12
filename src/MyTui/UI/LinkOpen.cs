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

    internal static bool TryReveal(string path, bool select)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return TryLaunch(Path.GetFullPath(path), select);
    }

    internal static ProcessStartInfo? StartInfo(string target, bool reveal = false)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(target) { UseShellExecute = true };
        }

        string? program = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsLinux() ? "xdg-open" : null;
        if (program is null)
        {
            return null;
        }

        var info = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (reveal && OperatingSystem.IsMacOS())
        {
            info.ArgumentList.Add("-R");
        }
        else if (reveal && OperatingSystem.IsLinux() && !Directory.Exists(target))
        {
            // xdg-open has no portable "select item" option.
            target = Path.GetDirectoryName(target)!;
        }

        info.ArgumentList.Add(target);
        return info;
    }

    private static bool TryLaunch(string target, bool reveal = false)
    {
        try
        {
            ProcessStartInfo? info = StartInfo(target, reveal);
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
