using System.Collections;
using System.Runtime.InteropServices;

namespace MyTui.Terminal;

/// <summary>
/// One Unix process on a PTY. <see cref="System.Diagnostics.Process"/> cannot allocate a pty.
/// posix_spawn is used so the child never returns into this multithreaded runtime.
/// </summary>
internal sealed class PtySession : IDisposable
{
    private readonly Lock _gate = new();
    private readonly int _pid;
    private int _master;
    private Thread? _reader;
    private volatile bool _stop;
    private int _reaped;
    private bool _disposed;

    private PtySession(int master, int pid)
    {
        _master = master;
        _pid = pid;
    }

    internal int Pid => _pid;

    internal int? ExitCode { get; private set; }

    internal event Action<byte[]>? Output;

    internal static PtySession Start(
        string file,
        IReadOnlyList<string> arguments,
        int columns,
        int rows,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The Phase 0 PTY host supports macOS and Linux.");
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("A command path is required.", nameof(file));
        }

        if (columns < 1 || rows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "Columns and rows must be at least 1.");
        }

        if (!File.Exists(file))
        {
            throw new FileNotFoundException("Command was not found.", file);
        }

        int master = PtyNative.PosixOpenPt(OpenFlags());
        if (master < 0)
        {
            throw new IOException(PtyNative.Describe("posix_openpt"));
        }

        PtySession? session = null;
        try
        {
            if (PtyNative.GrantPt(master) < 0)
            {
                throw new IOException(PtyNative.Describe("grantpt"));
            }

            if (PtyNative.UnlockPt(master) < 0)
            {
                throw new IOException(PtyNative.Describe("unlockpt"));
            }

            string slaveName = Marshal.PtrToStringUTF8(PtyNative.PtsName(master))
                ?? throw new IOException(PtyNative.Describe("ptsname"));
            // macOS rejects TIOCSWINSZ until the slave has been opened at least once.
            int slave = PtyNative.OpenPath(slaveName, PtyNative.SlaveOpenFlags);
            if (slave < 0)
            {
                throw new IOException(PtyNative.Describe("open slave"));
            }

            PtyNative.Close(slave);
            PtyNative.SetWindowSize(master, columns, rows);

            string[] argv = arguments.Count == 0 ? [file] : arguments.ToArray();
            using var actions = new SpawnActions();
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                actions.Chdir(workingDirectory);
            }

            actions.Open(0, slaveName);
            actions.Dup(0, 1);
            actions.Dup(0, 2);
            using var attributes = new SpawnAttributes();
            attributes.SetSid();
            using var args = new NativeStringList(argv);
            using var envp = new NativeStringList(FormatEnvironment(environment));
            int spawned = PtyNative.Spawn(out int pid, file, actions.Handle, attributes.Handle, args.Pointer, envp.Pointer);
            if (spawned != 0)
            {
                throw new IOException($"posix_spawn failed: {Marshal.GetPInvokeErrorMessage(spawned)} ({spawned})");
            }

            var started = new PtySession(master, pid);
            session = started;
            started.HoldSize(columns, rows);
            return started;
        }
        catch
        {
            if (session is not null)
            {
                session.Dispose();
            }
            else
            {
                PtyNative.Close(master);
            }

            throw;
        }
    }

    internal void StartReading()
    {
        lock (_gate)
        {
            if (_disposed || _reader is not null)
            {
                return;
            }

            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "pty-reader",
            };
            _reader.Start();
        }
    }

    internal void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        byte[] copy = data.ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GCHandle pin = GCHandle.Alloc(copy, GCHandleType.Pinned);
            try
            {
                int offset = 0;
                while (offset < copy.Length)
                {
                    nint written = PtyNative.Write(_master, pin.AddrOfPinnedObject() + offset, (nuint)(copy.Length - offset));
                    if (written < 0)
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error == PtyNative.Eintr)
                        {
                            continue;
                        }

                        return;
                    }

                    offset += (int)written;
                }
            }
            finally
            {
                pin.Free();
            }
        }
    }

    internal void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _master < 0)
            {
                return;
            }

            HoldSize(columns, rows);
        }
    }

    private void HoldSize(int columns, int rows)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            PtyNative.SetWindowSize(_master, columns, rows);
            if (PtyNative.ReadWindowSize(_master, out int gotColumns, out int gotRows)
                && gotColumns == columns
                && gotRows == rows)
            {
                if (_pid > 1)
                {
                    _ = PtyNative.Kill(-_pid, PtyNative.SigWinch);
                }

                return;
            }

            Thread.Sleep(5);
        }

        throw new IOException($"The kernel did not keep the window size {columns}x{rows}.");
    }

    internal bool PollExited()
    {
        if (_pid <= 0 || Volatile.Read(ref _reaped) != 0)
        {
            return true;
        }

        int waited = PtyNative.WaitPid(_pid, out int status, PtyNative.WaitNoHang);
        if (waited == _pid || (waited < 0 && Marshal.GetLastPInvokeError() == PtyNative.Echild))
        {
            if (waited == _pid)
            {
                Remember(status);
            }

            Interlocked.Exchange(ref _reaped, 1);
            return true;
        }

        return false;
    }

    internal static bool IsRunning(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        if (PtyNative.Kill(pid, 0) == 0)
        {
            return true;
        }

        return Marshal.GetLastPInvokeError() != PtyNative.Esrch;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stop = true;
        Signal(PtyNative.SigTerm);
        if (_reader is not null && !_reader.Join(TimeSpan.FromMilliseconds(700)))
        {
            Signal(PtyNative.SigKill);
            _reader.Join(TimeSpan.FromMilliseconds(700));
        }

        lock (_gate)
        {
            if (_master >= 0)
            {
                PtyNative.Close(_master);
                _master = -1;
            }
        }

        Reap();
    }

    private void ReadLoop()
    {
        byte[] buffer = new byte[4096];
        while (!_stop)
        {
            int fd;
            lock (_gate)
            {
                fd = _master;
            }

            if (fd < 0)
            {
                break;
            }

            var poll = new PtyNative.PollFd { Fd = fd, Events = PtyNative.PollIn };
            int ready = PtyNative.Poll(ref poll, 1, 50);
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == PtyNative.Eintr)
                {
                    continue;
                }

                break;
            }

            if (ready == 0 || _stop)
            {
                continue;
            }

            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            int count;
            try
            {
                count = (int)PtyNative.Read(fd, pin.AddrOfPinnedObject(), (nuint)buffer.Length);
            }
            finally
            {
                pin.Free();
            }

            if (count < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is PtyNative.Eintr || error == PtyNative.Again)
                {
                    continue;
                }

                break;
            }

            if (count == 0)
            {
                break;
            }

            byte[] copy = new byte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            try
            {
                Output?.Invoke(copy);
            }
            catch (Exception)
            {
                // The UI is going away. Keep the process until Dispose.
            }
        }
    }

    private void Signal(int signal)
    {
        if (_pid <= 1)
        {
            return;
        }

        if (PtyNative.Kill(-_pid, signal) != 0)
        {
            _ = PtyNative.Kill(_pid, signal);
        }
    }

    private void Reap()
    {
        if (_pid <= 0 || Interlocked.Exchange(ref _reaped, 1) != 0)
        {
            return;
        }

        long deadline = Environment.TickCount64 + 500;
        while (Environment.TickCount64 < deadline)
        {
            int waited = PtyNative.WaitPid(_pid, out int status, PtyNative.WaitNoHang);
            if (waited == _pid || (waited < 0 && Marshal.GetLastPInvokeError() == PtyNative.Echild))
            {
                if (waited == _pid)
                {
                    Remember(status);
                }

                return;
            }

            if (waited < 0 && Marshal.GetLastPInvokeError() != PtyNative.Eintr)
            {
                break;
            }

            Thread.Sleep(20);
        }

        Signal(PtyNative.SigKill);
        while (true)
        {
            int waited = PtyNative.WaitPid(_pid, out int status, 0);
            if (waited == _pid)
            {
                Remember(status);
                return;
            }

            if (waited < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == PtyNative.Echild)
                {
                    return;
                }

                if (error == PtyNative.Eintr)
                {
                    continue;
                }

                throw new IOException(PtyNative.Describe("waitpid"));
            }
        }
    }

    private static int OpenFlags() => PtyNative.OpenFlags;

    private static List<string> FormatEnvironment(IReadOnlyDictionary<string, string>? overrides)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value && Accept(key, value))
            {
                environment[key] = value;
            }
        }

        environment["TERM"] = "xterm-256color";
        if (overrides is not null)
        {
            foreach ((string key, string value) in overrides)
            {
                if (Accept(key, value))
                {
                    environment[key] = value;
                }
            }
        }

        return environment.Select(pair => pair.Key + "=" + pair.Value).ToList();
    }

    private static bool Accept(string key, string value) =>
        key.Length > 0 && !key.Contains('=') && !key.Contains('\0') && !value.Contains('\0');

    /// <summary>
    /// Storage for a spawn file-action or attribute object.
    /// Callers pass this address through. macOS keeps a pointer in the slot.
    /// Linux keeps the struct in the buffer. Dereferencing the slot and passing that value crashes addopen.
    /// </summary>
    private sealed class SpawnActions : IDisposable
    {
        private readonly IntPtr _storage = Allocate();

        internal SpawnActions()
        {
            try
            {
                ThrowIf(PtyNative.SpawnActionsInit(_storage), "posix_spawn_file_actions_init");
            }
            catch
            {
                Marshal.FreeHGlobal(_storage);
                throw;
            }
        }

        internal IntPtr Handle => _storage;

        internal void Open(int fd, string path) =>
            ThrowIf(PtyNative.SpawnActionsAddOpen(Handle, fd, path, 2, 0), "posix_spawn_file_actions_addopen");

        internal void Chdir(string path) =>
            ThrowIf(PtyNative.AddChdir(Handle, path), "posix_spawn_file_actions_addchdir");

        internal void Dup(int fd, int newFd) =>
            ThrowIf(PtyNative.SpawnActionsAddDup2(Handle, fd, newFd), "posix_spawn_file_actions_adddup2");

        public void Dispose()
        {
            _ = PtyNative.SpawnActionsDestroy(_storage);
            Marshal.FreeHGlobal(_storage);
        }
    }

    private sealed class SpawnAttributes : IDisposable
    {
        private readonly IntPtr _storage = Allocate();

        internal SpawnAttributes()
        {
            try
            {
                ThrowIf(PtyNative.SpawnAttrInit(_storage), "posix_spawnattr_init");
            }
            catch
            {
                Marshal.FreeHGlobal(_storage);
                throw;
            }
        }

        internal IntPtr Handle => _storage;

        internal void SetSid() =>
            ThrowIf(PtyNative.SpawnAttrSetFlags(Handle, PtyNative.SpawnSetSid), "posix_spawnattr_setflags");

        public void Dispose()
        {
            _ = PtyNative.SpawnAttrDestroy(_storage);
            Marshal.FreeHGlobal(_storage);
        }
    }

    private static IntPtr Allocate()
    {
        int bytes = OperatingSystem.IsMacOS() ? IntPtr.Size : 1024;
        IntPtr pointer = Marshal.AllocHGlobal(bytes);
        for (int i = 0; i < bytes; i++)
        {
            Marshal.WriteByte(pointer, i, 0);
        }

        return pointer;
    }

    private void Remember(int status) => ExitCode ??= DecodeWaitStatus(status);

    internal static int DecodeWaitStatus(int status)
    {
        int signal = status & 0x7f;
        if (signal == 0 || signal == 0x7f)
        {
            return (status >> 8) & 0xff;
        }

        return 128 + signal;
    }

    private static void ThrowIf(int error, string call)
    {
        if (error != 0)
        {
            throw new IOException($"{call} failed: {Marshal.GetPInvokeErrorMessage(error)} ({error})");
        }
    }

    private sealed class NativeStringList : IDisposable
    {
        private readonly IntPtr[] _items;

        internal NativeStringList(IReadOnlyList<string> values)
        {
            _items = new IntPtr[values.Count];
            Pointer = Marshal.AllocHGlobal(IntPtr.Size * (values.Count + 1));
            for (int i = 0; i < values.Count; i++)
            {
                _items[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
                Marshal.WriteIntPtr(Pointer, i * IntPtr.Size, _items[i]);
            }

            Marshal.WriteIntPtr(Pointer, values.Count * IntPtr.Size, IntPtr.Zero);
        }

        internal IntPtr Pointer { get; }

        public void Dispose()
        {
            foreach (IntPtr item in _items)
            {
                if (item != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(item);
                }
            }

            Marshal.FreeHGlobal(Pointer);
        }
    }
}
