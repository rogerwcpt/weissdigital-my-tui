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

    internal string? StartError { get; set; }

    internal bool IsRunning => Terminal is { } process && !process.HasExited && process.IsRunning;

    internal string ListLabel
    {
        get
        {
            if (Terminal is null)
            {
                return Name + " · stopped";
            }

            return Terminal.HasExited ? Name + " · exited" : Name;
        }
    }

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

    internal void Stop()
    {
        TerminalSession? process = Terminal;
        Terminal = null;
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
