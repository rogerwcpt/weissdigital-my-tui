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

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    internal static partial int SpawnActionsDestroy(IntPtr actions);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    internal static partial int SpawnAttrInit(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    internal static partial int SpawnAttrSetFlags(IntPtr attributes, short flags);

    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    internal static partial int SpawnAttrDestroy(IntPtr attributes);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    internal static partial int IoCtlWindow(int fd, nuint request, ref UnixWinsize size);

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
