using System.Text.Json;

namespace MyTui.Persistence;

internal sealed class SessionTemplate
{
    internal string Name { get; init; } = "";

    internal string Cwd { get; init; } = "";

    internal string Command { get; init; } = "";

    internal IReadOnlyList<string> Args { get; init; } = [];

    internal IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// ~/.config/my-tui/templates.json, beside state.json. A bad file is parked. Sessions are not touched.
/// Applying a template only fills the new-session dialog.
/// </summary>
internal sealed class TemplateStore
{
    private readonly string _path;
    private bool _leaveFileAlone;

    internal TemplateStore(string path) => _path = path;

    internal string FilePath => _path;

    internal static string DefaultPath()
    {
        string? directory = Path.GetDirectoryName(StateStore.DefaultPath());
        return Path.Combine(directory ?? "", "templates.json");
    }

    internal IReadOnlyList<SessionTemplate> Load()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(_path);
            PersistedTemplates? file = JsonSerializer.Deserialize<PersistedTemplates>(json, StateStore.Options);
            if (file?.Templates is null)
            {
                return [];
            }

            var templates = new List<SessionTemplate>();
            foreach (PersistedTemplate dto in file.Templates)
            {
                if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Command))
                {
                    continue;
                }

                templates.Add(new SessionTemplate
                {
                    Name = dto.Name.Trim(),
                    Cwd = dto.Cwd ?? "",
                    Command = dto.Command.Trim(),
                    Args = dto.Args?.Where(arg => !string.IsNullOrEmpty(arg)).ToArray() ?? [],
                    Env = Clean(dto.Env),
                });
            }

            return templates;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _leaveFileAlone = !TryPark();
            return [];
        }
    }

    internal void Save(IReadOnlyList<SessionTemplate> templates)
    {
        if (_leaveFileAlone)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new PersistedTemplates
        {
            Templates = templates.Select(template => new PersistedTemplate
            {
                Name = template.Name,
                Cwd = template.Cwd,
                Command = template.Command,
                Args = [.. template.Args],
                Env = Clean(template.Env),
            }).ToList(),
        };
        string json = JsonSerializer.Serialize(file, StateStore.Options);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }

    internal void Upsert(SessionTemplate template)
    {
        var templates = Load().ToList();
        int index = templates.FindIndex(item => string.Equals(item.Name, template.Name, StringComparison.Ordinal));
        if (index >= 0)
        {
            templates[index] = template;
        }
        else
        {
            templates.Add(template);
        }

        Save(templates);
    }

    internal void RemoveAt(int index)
    {
        var templates = Load().ToList();
        if (index < 0 || index >= templates.Count)
        {
            return;
        }

        templates.RemoveAt(index);
        Save(templates);
    }

    private bool TryPark()
    {
        try
        {
            string backup = _path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string>? source)
    {
        var env = new Dictionary<string, string>();
        if (source is null)
        {
            return env;
        }

        foreach ((string key, string value) in source)
        {
            if (key.Length == 0 || key.Contains('=') || key.Contains('\0') || value.Contains('\0'))
            {
                continue;
            }

            env[key] = value;
        }

        return env;
    }

    private sealed class PersistedTemplates
    {
        public List<PersistedTemplate> Templates { get; set; } = [];
    }

    private sealed class PersistedTemplate
    {
        public string Name { get; set; } = "";

        public string Cwd { get; set; } = "";

        public string Command { get; set; } = "";

        public List<string> Args { get; set; } = [];

        public Dictionary<string, string> Env { get; set; } = [];
    }
}
