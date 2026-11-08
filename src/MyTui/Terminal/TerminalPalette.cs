using System.Globalization;
using System.Text.Json;
using Ghostty.Vt.Types;

namespace MyTui.Terminal;

/// <summary>
/// The 16 ANSI colors every session starts with. <c>terminalPalette</c> in config.json is a preset name,
/// or an array of up to 16 <c>#RRGGBB</c> strings laid over VS Code Dark+. Colors 16–255 stay the standard cube.
/// </summary>
internal static class TerminalPalette
{
    internal const string Default = "vscode";

    private static readonly Dictionary<string, uint[]> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vscode"] =
        [
            0x000000, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
            0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xFFFFFF,
        ],
        ["xterm"] =
        [
            0x000000, 0xCD0000, 0x00CD00, 0xCDCD00, 0x0000EE, 0xCD00CD, 0x00CDCD, 0xE5E5E5,
            0x7F7F7F, 0xFF0000, 0x00FF00, 0xFFFF00, 0x5C5CFF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
        ],
        ["tomorrow-night"] =
        [
            0x1D1F21, 0xCC6666, 0xB5BD68, 0xF0C674, 0x81A2BE, 0xB294BB, 0x8ABEB7, 0xC5C8C6,
            0x666666, 0xD54E53, 0xB9CA4A, 0xE7C547, 0x7AA6DA, 0xC397D8, 0x70C0B1, 0xEAEAEA,
        ],
    };

    private static uint[] _ansi = Presets[Default];

    /// <summary>Reads <c>terminalPalette</c>. A missing, unknown, or malformed value keeps VS Code Dark+.</summary>
    internal static void Load(string path)
    {
        _ansi = Presets[Default];
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("terminalPalette", out JsonElement value))
            {
                _ansi = Parse(value) ?? Presets[Default];
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Copies <paramref name="basis"/> and replaces its first 16 entries.</summary>
    internal static ColorRgb[] Apply(ColorRgb[] basis)
    {
        var palette = (ColorRgb[])basis.Clone();
        for (int i = 0; i < _ansi.Length && i < palette.Length; i++)
        {
            uint rgb = _ansi[i];
            palette[i] = new ColorRgb { R = (byte)(rgb >> 16), G = (byte)(rgb >> 8), B = (byte)rgb };
        }

        return palette;
    }

    private static uint[]? Parse(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return Presets.TryGetValue(value.GetString()!.Trim(), out uint[]? preset) ? preset : null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        uint[] colors = (uint[])Presets[Default].Clone();
        int index = 0;
        foreach (JsonElement entry in value.EnumerateArray())
        {
            if (index >= colors.Length)
            {
                break;
            }

            if (entry.ValueKind != JsonValueKind.String || !TryHex(entry.GetString()!, out uint rgb))
            {
                return null;
            }

            colors[index++] = rgb;
        }

        return colors;
    }

    private static bool TryHex(string text, out uint rgb)
    {
        string trimmed = text.Trim();
        rgb = 0;
        return trimmed.Length == 7
            && trimmed[0] == '#'
            && uint.TryParse(trimmed.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }
}
