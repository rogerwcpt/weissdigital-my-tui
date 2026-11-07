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

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
                return true;
            }

            string? program = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsLinux() ? "xdg-open" : null;
            if (program is null)
            {
                return false;
            }

            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = program,
                ArgumentList = { url },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return process is not null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }
}
