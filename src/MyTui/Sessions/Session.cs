using MyTui.Persistence;
using MyTui.Terminal;

namespace MyTui.Sessions;

/// <summary>
/// One saved session plus the process that exists only while this launch is running.
/// Pid, exit code, and the inherited environment are not part of the saved shape.
/// </summary>
internal sealed class Session
{
    private Session()
    {
    }

    internal Guid Id { get; private init; }

    internal string Name { get; set; } = "";

    internal string Cwd { get; set; } = "";

    internal string Command { get; set; } = "";

    internal List<string> Args { get; } = [];

    internal Dictionary<string, string> Env { get; } = [];

    internal DateTimeOffset Created { get; private init; }

    internal SessionTab Tab { get; set; } = SessionTab.Terminal;

    internal TerminalSession? Terminal { get; private set; }

    internal string? SelectedPath { get; set; }

    internal ScriptRun? Script { get; private set; }

    internal bool ShowingScriptOutput { get; set; }

    internal string? StartError { get; set; }

    internal bool IsRunning => Terminal is { } process && !process.HasExited && process.IsRunning;

    /// <summary>
    /// A shell is busy while a command is in the foreground or it still has a child process.
    /// Any other command is busy for as long as it is running.
    /// </summary>
    internal bool IsBusy => IsBusySession(Command, Args, IsRunning, Terminal?.IsBusy == true);

    internal string WindowTitle => Name;

    internal string ListLabel
    {
        get
        {
            if (Terminal is null)
            {
                return Name + " · stopped";
            }

            if (Terminal.HasExited)
            {
                return Name + " · exited";
            }

            return Name;
        }
    }

    /// <summary>Shells wait at a prompt. Everything else is the command the session was started to run.</summary>
    internal static bool IsBusySession(string command, IReadOnlyList<string> args, bool running, bool foregroundWork)
    {
        if (!running)
        {
            return false;
        }

        if (foregroundWork)
        {
            return true;
        }

        return !IsInteractiveShell(command, args);
    }

    private static bool IsInteractiveShell(string command, IReadOnlyList<string> args)
    {
        if (!IsShellName(Path.GetFileName(command)))
        {
            return false;
        }

        foreach (string arg in args)
        {
            if (arg is "-c" or "--command")
            {
                return false;
            }

            if (arg.Length > 1 && arg[0] == '-' && arg[1] != '-' && arg.Contains('c'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsShellName(string name) => name is "sh" or "bash" or "zsh" or "fish" or "dash" or "ksh" or "mksh" or "csh" or "tcsh";

    internal string StatusLine
    {
        get
        {
            if (!string.IsNullOrEmpty(StartError) && !IsRunning)
            {
                return Fit($"{Name}  {StartError}");
            }

            if (Terminal is { } process)
            {
                if (!process.HasExited)
                {
                    return Fit($"{Name}  {Cwd}  pid {process.Pid}");
                }

                string code = process.ExitCode?.ToString() ?? "?";
                return Fit($"{Name}  {Cwd}  exited {code}  Enter starts");
            }

            return Fit($"{Name}  {Cwd}  stopped  Enter starts");
        }
    }

    internal static Session Create(
        string name,
        string cwd,
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env)
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Cwd = cwd,
            Command = command.Trim(),
            Created = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };
        session.Args.AddRange(args);
        CopyEnv(env, session.Env);
        return session;
    }

    internal static bool TryRestore(PersistedSession dto, out Session session)
    {
        session = null!;
        if (!Guid.TryParse(dto.Id, out Guid id) || string.IsNullOrWhiteSpace(dto.Command))
        {
            return false;
        }

        string name = string.IsNullOrWhiteSpace(dto.Name) ? Path.GetFileName(dto.Command) : dto.Name.Trim();
        session = new Session
        {
            Id = id,
            Name = name,
            Cwd = string.IsNullOrWhiteSpace(dto.Cwd) ? "/" : dto.Cwd,
            Command = dto.Command.Trim(),
            Created = dto.Created,
            Tab = SessionTabNames.Parse(dto.LastFocusedTab),
        };
        if (dto.Args is not null)
        {
            session.Args.AddRange(dto.Args.Where(arg => !string.IsNullOrEmpty(arg)));
        }

        CopyEnv(dto.Env, session.Env);
        return true;
    }

    internal void Start(int columns, int rows)
    {
        if (IsRunning)
        {
            return;
        }

        Terminal?.Dispose();
        Terminal = null;
        if (!Directory.Exists(Cwd))
        {
            throw new IOException($"cwd is not a directory: {Cwd}");
        }

        string? file = CommandLine.Resolve(Command);
        if (file is null)
        {
            throw new IOException($"Cannot find command: {Command}");
        }

        List<string> argv = [file, .. Args];
        Terminal = TerminalSession.Start(
            file,
            argv,
            Math.Max(columns, 1),
            Math.Max(rows, 1),
            Name,
            Env,
            Cwd);
        StartError = null;
    }

    internal void RunScript(string path)
    {
        if (Script?.IsRunning == true)
        {
            throw new InvalidOperationException("A script is already running. Stop it before running another.");
        }

        ScriptRun run = ScriptRun.Start(Cwd, path, Env);
        Script?.Dispose();
        Script = run;
        ShowingScriptOutput = true;
    }

    internal void StopScript() => Script?.Stop();

    internal void Stop(bool force = false)
    {
        TerminalSession? process = Terminal;
        Terminal = null;
        if (force)
        {
            process?.Kill();
        }

        process?.Dispose();
    }

    internal PersistedSession ToPersisted() => new()
    {
        Id = Id.ToString("D"),
        Name = Name,
        Cwd = Cwd,
        Command = Command,
        Args = [.. Args],
        Env = new Dictionary<string, string>(Env),
        Created = Created,
        LastFocusedTab = SessionTabNames.Format(Tab),
    };

    private static void CopyEnv(IReadOnlyDictionary<string, string>? source, Dictionary<string, string> target)
    {
        if (source is null)
        {
            return;
        }

        foreach ((string key, string value) in source)
        {
            if (key.Length == 0 || key.Contains('=') || key.Contains('\0') || value.Contains('\0'))
            {
                continue;
            }

            target[key] = value;
        }
    }

    private static string Fit(string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 200 ? text : text[..200];
    }
}
