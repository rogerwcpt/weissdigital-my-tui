namespace MyTui.Sessions;

internal enum SessionTab
{
    Terminal = 0,
    Files = 1,
    Preview = 2,
}

internal static class SessionTabNames
{
    internal static string Format(SessionTab tab) => tab switch
    {
        SessionTab.Files => "files",
        SessionTab.Preview => "preview",
        _ => "terminal",
    };

    internal static SessionTab Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "files" => SessionTab.Files,
        "preview" => SessionTab.Preview,
        _ => SessionTab.Terminal,
    };
}
