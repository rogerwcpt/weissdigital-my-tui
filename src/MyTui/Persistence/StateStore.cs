using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTui.Persistence;

internal sealed class PersistedState
{
    public List<PersistedSession> Sessions { get; set; } = [];

    public string? LastFocusedSessionId { get; set; }

    public string LastFocusedTab { get; set; } = "terminal";

    /// <summary>Sessions panel width in columns. Missing means the default width.</summary>
    public int? SessionsWidth { get; set; }
}

internal sealed class PersistedSession
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Cwd { get; set; } = "";

    public string Command { get; set; } = "";

    public List<string> Args { get; set; } = [];

    public Dictionary<string, string> Env { get; set; } = [];

    public DateTimeOffset Created { get; set; }

    public string LastFocusedTab { get; set; } = "terminal";
}

/// <summary>
/// ~/.config/my-tui/state.json. A file that cannot be read is moved aside and the app starts empty.
/// </summary>
internal sealed class StateStore
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new UtcTimestampConverter() },
    };

    private readonly string _path;
    private bool _leaveFileAlone;

    internal StateStore(string path) => _path = path;

    internal string FilePath => _path;

    internal static string DefaultPath()
    {
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(home, ".config", "my-tui", "state.json");
    }

    internal PersistedState Load()
    {
        if (!File.Exists(_path))
        {
            return new PersistedState();
        }

        try
        {
            string json = File.ReadAllText(_path);
            PersistedState? state = JsonSerializer.Deserialize<PersistedState>(json, Options);
            if (state is null)
            {
                throw new JsonException("State was empty.");
            }

            state.Sessions ??= [];
            return state;
        }
        catch (Exception)
        {
            _leaveFileAlone = !TryPark();
            return new PersistedState();
        }
    }

    internal void Save(PersistedState state)
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

        string json = JsonSerializer.Serialize(state, Options);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }

    private bool TryPark()
    {
        try
        {
            string backup = _path + ".bad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            File.Move(_path, backup);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text)
                || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset value))
            {
                throw new JsonException("Bad timestamp.");
            }

            return value.ToUniversalTime();
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }
}
