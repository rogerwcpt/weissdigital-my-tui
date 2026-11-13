using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MyTui.Sessions;

/// <summary>A non-interactive script with bounded, live output, separate from the session PTY.</summary>
internal sealed class ScriptRun : IDisposable
{
    internal const int MaxOutputChars = 256 * 1024;

    private readonly object _gate = new();
    private readonly Process _process;
    private readonly CancellationTokenSource _cancel = new();
    private readonly StringBuilder _output = new();
    private bool _running = true;
    private bool _stopped;
    private bool _truncated;
    private int? _exitCode;
    private string? _error;
    private int _version = 1;

    private ScriptRun(Process process, string path, string cwd)
    {
        _process = process;
        Path = path;
        Cwd = cwd;
    }

    internal string Path { get; }
    internal string Cwd { get; }
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal bool IsRunning
    {
        get { lock (_gate) { return _running; } }
    }

    internal int Version
    {
        get { lock (_gate) { return _version; } }
    }

    internal readonly record struct Snapshot(int Version, string Text, string Status);

    internal Snapshot Capture()
    {
        lock (_gate)
        {
            string status = _running ? "running" : _stopped ? "stopped" : _error is not null ? "failed" : $"exited {_exitCode}";
            string text = $"Script: {Path}\nWorking directory: {Cwd}\n\n" + _output;
            if (_truncated)
            {
                text += "\n[output truncated]";
            }

            text += _running ? "\n\n[Running — F9 → Stop script]" : $"\n\n[{status}]";
            if (_error is not null)
            {
                text += "\n" + _error;
            }

            return new Snapshot(_version, text, status);
        }
    }

    internal static ScriptRun Start(string root, string path, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Running shell scripts requires macOS or Linux.");
        }

        path = System.IO.Path.GetFullPath(path);
        root = System.IO.Path.GetFullPath(root);
        if (!path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new IOException("Select an existing .sh file to run.");
        }

        if (!Workspace.IsInside(root, path))
        {
            throw new IOException("The script is outside the session directory.");
        }

        // Invoke the shebang interpreter explicitly so scripts need not have executable permission.
        // Without a shebang, .sh files use the portable system shell, not the user's login shell.
        string? firstLine;
        using (var reader = File.OpenText(path))
        {
            firstLine = reader.ReadLine();
        }

        IReadOnlyList<string> interpreter = firstLine?.StartsWith("#!", StringComparison.Ordinal) == true
            ? CommandLine.Split(firstLine[2..])
            : ["/bin/sh"];
        if (interpreter.Count == 0)
        {
            throw new IOException("The script has an empty shebang.");
        }

        var info = new ProcessStartInfo
        {
            FileName = interpreter[0],
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        foreach (string argument in interpreter.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        info.ArgumentList.Add(path);
        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            process.StandardInput.Close(); // Interactive scripts belong in Terminal.
        }
        catch
        {
            process.Dispose();
            throw;
        }

        var run = new ScriptRun(process, path, root);
        run.Completion = run.ReadOutputAsync();
        return run;
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (!_running || _stopped)
            {
                return;
            }

            // Kill descendants as well as the interpreter (e.g. a script waiting on sleep).
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { } // Exited between checking and killing.
            catch (Win32Exception ex)
            {
                _error = ex.Message;
                _version++;
                return;
            }

            _stopped = true;
            _cancel.Cancel();
        }
    }

    public void Dispose() => Stop();

    private async Task ReadOutputAsync()
    {
        Task stdout = PumpAsync(_process.StandardOutput);
        Task stderr = PumpAsync(_process.StandardError);
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            // A background descendant may keep a pipe open after the interpreter exits.
            try
            {
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _cancel.Cancel();
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            lock (_gate) { _error = ex.Message; }
        }
        finally
        {
            lock (_gate)
            {
                _exitCode = _process.HasExited ? _process.ExitCode : null;
                _running = false;
                _version++;
                _process.Dispose();
                _cancel.Dispose();
            }
        }
    }

    private async Task PumpAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), _cancel.Token).ConfigureAwait(false)) > 0)
        {
            lock (_gate)
            {
                int take = Math.Min(count, MaxOutputChars - _output.Length);
                if (take > 0)
                {
                    _output.Append(buffer, 0, take);
                }

                bool wasTruncated = _truncated;
                _truncated |= take < count;
                if (take > 0 || wasTruncated != _truncated)
                {
                    _version++;
                }
            }
        }
    }
}
