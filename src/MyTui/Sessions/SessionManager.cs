using MyTui.Persistence;

namespace MyTui.Sessions;

internal sealed class SessionManager
{
    private readonly List<Session> _sessions = [];
    private readonly StateStore _store;
    private int _focused;

    private SessionManager(StateStore store) => _store = store;

    internal IReadOnlyList<Session> Sessions => _sessions;

    internal int FocusedIndex => _focused;

    internal Session? Focused => _sessions.Count == 0 ? null : _sessions[_focused];

    internal static SessionManager Load(string path) => Load(new StateStore(path));

    internal static SessionManager Load(StateStore store)
    {
        PersistedState state = store.Load();
        var manager = new SessionManager(store);
        var seen = new HashSet<Guid>();
        foreach (PersistedSession dto in state.Sessions)
        {
            if (!Session.TryRestore(dto, out Session session) || !seen.Add(session.Id))
            {
                continue;
            }

            manager._sessions.Add(session);
        }

        int index = manager._sessions.FindIndex(session =>
            string.Equals(session.Id.ToString("D"), state.LastFocusedSessionId, StringComparison.OrdinalIgnoreCase));
        manager._focused = index >= 0 ? index : 0;
        return manager;
    }

    internal Session Create(
        string name,
        string cwd,
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var session = Session.Create(name, cwd, command, args, env);
        _sessions.Add(session);
        _focused = _sessions.Count - 1;
        Save();
        return session;
    }

    internal Session Duplicate(Session source)
    {
        var taken = _sessions.Select(session => session.Name).ToHashSet(StringComparer.Ordinal);
        for (int count = 2; ; count++)
        {
            string name = $"{source.Name}-{count}";
            if (!taken.Contains(name))
            {
                return Create(name, source.Cwd, source.Command, source.Args, source.Env);
            }
        }
    }

    internal void Close(Session session)
    {
        int index = _sessions.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        session.Stop();
        _sessions.RemoveAt(index);
        if (_sessions.Count == 0)
        {
            _focused = 0;
        }
        else if (_focused > index || _focused == _sessions.Count)
        {
            _focused--;
        }

        Save();
    }

    internal void Focus(int index)
    {
        if (index < 0 || index >= _sessions.Count || index == _focused)
        {
            return;
        }

        _focused = index;
        Save();
    }

    internal bool MoveFocused(int delta)
    {
        if (_sessions.Count < 2 || delta == 0)
        {
            return false;
        }

        int target = _focused + delta;
        if (target < 0 || target >= _sessions.Count)
        {
            return false;
        }

        (_sessions[_focused], _sessions[target]) = (_sessions[target], _sessions[_focused]);
        _focused = target;
        Save();
        return true;
    }

    internal void SortByName()
    {
        if (_sessions.Count < 2)
        {
            return;
        }

        Session focused = Focused!;
        Session[] sorted = _sessions.OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _sessions.Clear();
        _sessions.AddRange(sorted);
        _focused = _sessions.IndexOf(focused);
        Save();
    }

    internal void Update(Session session, string name, string cwd, string command, IReadOnlyList<string> args)
    {
        session.Name = name.Trim();
        session.Cwd = cwd;
        session.Command = command.Trim();
        session.Args.Clear();
        session.Args.AddRange(args);
        if (session.SelectedPath is not null && !Workspace.IsInside(session.Cwd, session.SelectedPath))
        {
            session.SelectedPath = null;
        }

        Save();
    }

    internal void Rename(Session session, string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed == session.Name)
        {
            return;
        }

        session.Name = trimmed;
        Save();
    }

    internal void SetTab(Session session, SessionTab tab)
    {
        if (session.Tab == tab)
        {
            return;
        }

        session.Tab = tab;
        Save();
    }

    private void Save() => _store.Save(Snapshot());

    private PersistedState Snapshot()
    {
        var state = new PersistedState
        {
            LastFocusedSessionId = Focused?.Id.ToString("D"),
            LastFocusedTab = SessionTabNames.Format(Focused?.Tab ?? SessionTab.Terminal),
        };
        foreach (Session session in _sessions)
        {
            state.Sessions.Add(session.ToPersisted());
        }

        return state;
    }
}
