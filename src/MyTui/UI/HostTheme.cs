using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MyTui.Persistence;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using TuiAttribute = global::Terminal.Gui.Drawing.Attribute;

namespace MyTui.UI;

/// <summary>
/// Names the chooser can store. Terminal, Graphite, and Paper are ours.
/// The rest are Terminal.Gui themes, spelled the way <c>SwitchTheme</c> expects.
/// </summary>
internal static class ThemeNames
{
    internal const string Terminal = "Terminal";

    internal const string Graphite = "Graphite";

    internal const string Paper = "Paper";

    internal static readonly string[] All =
    [
        Terminal,
        Graphite,
        Paper,
        "TurboPascal 5",
        "Anders",
        "Dark",
        "Light",
        "Green Phosphor",
        "Amber Phosphor",
        "8-Bit",
    ];

    /// <summary>The stored spelling, or Terminal when the file names something this app does not ship.</summary>
    internal static string Canonical(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Terminal;
        }

        foreach (string known in All)
        {
            if (string.Equals(known, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return Terminal;
    }
}

/// <summary>
/// Chrome colors. Terminal keeps the terminal's own colors. Graphite and Paper are fixed palettes.
/// A library theme is whatever Terminal.Gui ships under that name. <c>NO_COLOR</c> and <c>TERM=dumb</c>
/// paint Terminal no matter what is stored.
/// </summary>
internal static class HostTheme
{
    private static Scheme _scheme = Plain();

    internal static bool ColorEnabled =>
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    internal static string Current { get; private set; } = ThemeNames.Terminal;

    internal static string ConfigPath()
    {
        string? directory = Path.GetDirectoryName(StateStore.DefaultPath());
        return Path.Combine(directory ?? "", "config.json");
    }

    /// <summary>Reads <c>theme</c> from config and installs it. Call this after <c>Application.Init</c>.</summary>
    internal static void Load(string? path = null) => Apply(ReadTheme(path ?? ConfigPath()));

    internal static void Save(string? path = null) => WriteTheme(path ?? ConfigPath(), Current);

    internal static void Apply(string? name)
    {
        string requested = ThemeNames.Canonical(name);
        Scheme scheme;
        if (!ColorEnabled || requested == ThemeNames.Terminal)
        {
            scheme = Plain();
        }
        else if (requested is ThemeNames.Graphite or ThemeNames.Paper)
        {
            scheme = Palette(requested);
        }
        else if (!TryLibrary(requested, out scheme))
        {
            requested = ThemeNames.Terminal;
            scheme = Plain();
        }

        _scheme = scheme;
        Current = requested;
        SchemeManager.AddScheme("Base", scheme);
        SchemeManager.AddScheme("Dialog", scheme);
        SchemeManager.AddScheme("Menu", scheme);
        SchemeManager.AddScheme("Error", scheme);
    }

    internal static Scheme Scheme() => _scheme;

    /// <summary>The palette for a name, ignoring <c>NO_COLOR</c>. Terminal and unknown names are the terminal colors.</summary>
    internal static Scheme Palette(string? name)
    {
        string requested = ThemeNames.Canonical(name);
        if (requested == ThemeNames.Graphite)
        {
            return Graphite();
        }

        if (requested == ThemeNames.Paper)
        {
            return Paper();
        }

        return Plain();
    }

    /// <summary>
    /// Preview colors follow the chrome. Every code role is set, because an unset role
    /// lets Terminal.Gui dim the text into a gray field.
    /// </summary>
    internal static Scheme Preview()
    {
        TuiAttribute ink = _scheme.Normal;
        // Selection: the plain-text view draws it with Highlight and Markdown with Focus. Theme values are
        // often accent ink on the same background, which is hard to see, so both use reverse video.
        TuiAttribute selection = ink with { Style = ink.Style | TextStyle.Reverse };
        return new Scheme
        {
            Normal = ink,
            HotNormal = _scheme.HotNormal,
            Focus = selection,
            HotFocus = _scheme.HotFocus,
            Active = _scheme.Active,
            HotActive = _scheme.HotActive,
            Highlight = selection,
            Editable = ink,
            ReadOnly = ink,
            Disabled = _scheme.Disabled,
            Code = ink,
            CodeComment = ink,
            CodeKeyword = ink,
            CodeString = ink,
            CodeNumber = ink,
            CodeOperator = ink,
            CodeType = ink,
            CodePreprocessor = ink,
            CodeIdentifier = ink,
            CodeConstant = ink,
            CodePunctuation = ink,
            CodeFunctionName = ink,
            CodeAttribute = ink,
        };
    }

    internal static Scheme Plain()
    {
        var plain = new TuiAttribute(Color.None, Color.None);
        var reverse = new TuiAttribute(Color.None, Color.None, TextStyle.Reverse);
        return new Scheme
        {
            Normal = plain,
            HotNormal = plain,
            Focus = reverse,
            HotFocus = reverse,
            Active = reverse,
            HotActive = reverse,
            Highlight = reverse,
            Editable = plain,
            ReadOnly = plain,
            Disabled = plain,
        };
    }

    /// <summary>Dark neutral palette. Teal is the accent on the focused row and the hot key.</summary>
    private static Scheme Graphite() => Colored("#C9CCD1", "#1E2127", "#1E2127", "#4FB8A8", "#5C6370");

    /// <summary>Light palette. Teal accent on a paper background.</summary>
    private static Scheme Paper() => Colored("#1F2328", "#F6F8FA", "#F6F8FA", "#0F6E6A", "#6E7781");

    private static Scheme Colored(string ink, string background, string focusInk, string focusBackground, string muted)
    {
        var normal = new TuiAttribute(ink, background);
        var focus = new TuiAttribute(focusInk, focusBackground, TextStyle.Bold);
        var hot = new TuiAttribute(focusBackground, background, TextStyle.Bold);
        var quiet = new TuiAttribute(muted, background, TextStyle.Faint);
        return new Scheme
        {
            Normal = normal,
            HotNormal = hot,
            Focus = focus,
            HotFocus = focus,
            Active = focus,
            HotActive = focus,
            Highlight = hot,
            Editable = normal,
            ReadOnly = normal,
            Disabled = quiet,
        };
    }

    private static bool TryLibrary(string name, out Scheme scheme)
    {
        scheme = Plain();
        try
        {
            // AddScheme survives SwitchTheme, so the previous palette would hide this one.
            // SetRuntimeSchemes is internal to Terminal.Gui. Clearing it lets the library theme load.
            ClearRuntimeSchemes();
            if (!TuiConfigurationBuilder.Shared.ThemeManager.SwitchTheme(name))
            {
                return false;
            }

            Scheme? basis = SchemeManager.GetScheme(Schemes.Base);
            if (basis is null)
            {
                return false;
            }

            scheme = basis;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ClearRuntimeSchemes()
    {
        MethodInfo? method = typeof(SchemeManager).GetMethod(
            "SetRuntimeSchemes",
            BindingFlags.Static | BindingFlags.NonPublic);
        method?.Invoke(null, [new Dictionary<string, Scheme?>(StringComparer.InvariantCultureIgnoreCase)]);
    }

    private static string? ReadTheme(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("theme", out JsonElement theme)
                && theme.ValueKind == JsonValueKind.String)
            {
                return theme.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static void WriteTheme(string path, string name)
    {
        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }
        else
        {
            root = new JsonObject();
        }

        root["theme"] = name;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
