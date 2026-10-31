using System.Runtime.InteropServices;

namespace MyTui.Terminal;

/// <summary>
/// libc surface for a Unix PTY. macOS request numbers were read from the headers on this machine.
/// Linux numbers are the glibc / asm-generic values.
/// </summary>
internal static partial class PtyNative
{
    internal const int Eintr = 4;
    internal const int Echild = 10;
    internal const int Esrch = 3;
    internal const int SigTerm = 15;
    internal const int SigKill = 9;
    internal const int WaitNoHang = 1;
    internal const short PollIn = 1;

    internal static int OpenFlags => OperatingSystem.IsMacOS()
        ? 2 | 0x20000 | 0x01000000
        : 2 | 0x100 | 0x80000;

    /// <summary>Open the slave once so macOS accepts TIOCSWINSZ on the master. O_NOCTTY keeps it from becoming ours.</summary>
    internal static int SlaveOpenFlags => OperatingSystem.IsMacOS() ? 2 | 0x20000 : 2 | 0x100;

    internal static nuint WindowSizeRequest => OperatingSystem.IsMacOS() ? 0x80087467 : 0x5414u;

    internal static nuint WindowSizeQuery => OperatingSystem.IsMacOS() ? 0x40087468 : 0x5413u;

    internal const int SigWinch = 28;

    private static readonly IntPtr MacIoctl = OperatingSystem.IsMacOS() ? LoadMacIoctl() : IntPtr.Zero;

    /// <summary>macOS value is from spawn.h on this machine. Linux value is glibc's POSIX_SPAWN_SETSID.</summary>
    internal static short SpawnSetSid => OperatingSystem.IsMacOS() ? (short)0x400 : (short)0x80;

    internal static int Again => OperatingSystem.IsMacOS() ? 35 : 11;

    internal static string Describe(string call)
    {
        int error = Marshal.GetLastPInvokeError();
        return $"{call} failed: {Marshal.GetPInvokeErrorMessage(error)} (errno {error})";
    }

    [LibraryImport("libc", EntryPoint = "posix_openpt", SetLastError = true)]
    internal static partial int PosixOpenPt(int flags);

    [LibraryImport("libc", EntryPoint = "grantpt", SetLastError = true)]
    internal static partial int GrantPt(int fd);

    [LibraryImport("libc", EntryPoint = "unlockpt", SetLastError = true)]
    internal static partial int UnlockPt(int fd);

    [LibraryImport("libc", EntryPoint = "ptsname", SetLastError = true)]
    internal static partial IntPtr PtsName(int fd);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int OpenPath(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);

    [LibraryImport("libc", EntryPoint = "posix_spawn", SetLastError = false, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Spawn(out int pid, string path, IntPtr actions, IntPtr attributes, IntPtr argv, IntPtr envp);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    internal static partial int SpawnActionsInit(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addopen", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SpawnActionsAddOpen(IntPtr actions, int fd, string path, int flags, uint mode);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    internal static partial int SpawnActionsAddDup2(IntPtr actions, int fd, int newFd);

    /// <summary>
    /// macOS 26 replaced addchdir_np with addchdir. Linux keeps the glibc _np name.
    /// The unused import is not called, so the other platform does not have to export it.
    /// </summary>
    internal static int AddChdir(IntPtr actions, string path) =>
        OperatingSystem.IsMacOS()
            ? SpawnActionsAddChdir(actions, path)
            : SpawnActionsAddChdirNp(actions, path);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SpawnActionsAddChdir(IntPtr actions, string path);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SpawnActionsAddChdirNp(IntPtr actions, string path);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    internal static partial int SpawnActionsDestroy(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    internal static partial int SpawnAttrInit(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    internal static partial int SpawnAttrSetFlags(IntPtr attributes, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    internal static partial int SpawnAttrDestroy(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoCtlWindow(int fd, nuint request, ref UnixWinsize size);

    internal static void SetWindowSize(int fd, int columns, int rows)
    {
        var size = new UnixWinsize
        {
            Rows = (ushort)Math.Min(rows, ushort.MaxValue),
            Columns = (ushort)Math.Min(columns, ushort.MaxValue),
        };
        int result = WindowIoctl(fd, WindowSizeRequest, ref size);
        if (result < 0)
        {
            throw new IOException(Describe("TIOCSWINSZ"));
        }
    }

    internal static bool ReadWindowSize(int fd, out int columns, out int rows)
    {
        var size = new UnixWinsize();
        int result = WindowIoctl(fd, WindowSizeQuery, ref size);
        columns = size.Columns;
        rows = size.Rows;
        return result >= 0;
    }

    private static int WindowIoctl(int fd, nuint request, ref UnixWinsize size)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return IoCtlWindow(fd, request, ref size);
        }

        unsafe
        {
            fixed (UnixWinsize* pointer = &size)
            {
                var ioctl = (delegate* unmanaged[Cdecl]<int, nuint, UnixWinsize*, int>)MacIoctl;
                return ioctl(fd, request, pointer);
            }
        }
    }

    private static IntPtr LoadMacIoctl()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "libmytui_pty.dylib");
        if (!File.Exists(path))
        {
            string? directory = Path.GetDirectoryName(typeof(PtyNative).Assembly.Location);
            if (!string.IsNullOrEmpty(directory))
            {
                path = Path.Combine(directory, "libmytui_pty.dylib");
            }
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "libmytui_pty.dylib was not built next to my-tui. The macOS window-size call needs that shim.",
                path);
        }

        return NativeLibrary.GetExport(NativeLibrary.Load(path), "mytui_ioctl_winsize");
    }

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static partial nint Read(int fd, IntPtr buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
    internal static partial nint Write(int fd, IntPtr buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    internal static partial int Poll(ref PollFd fd, uint count, int timeoutMs);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    internal static partial int Kill(int pid, int signal);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    internal static partial int WaitPid(int pid, out int status, int options);

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnixWinsize
    {
        internal ushort Rows;
        internal ushort Columns;
        internal ushort XPixel;
        internal ushort YPixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        internal int Fd;
        internal short Events;
        internal short REvents;
    }
}
