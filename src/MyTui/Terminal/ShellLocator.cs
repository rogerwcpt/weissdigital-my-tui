namespace MyTui.Terminal;

internal static class ShellLocator
{
    internal static string Resolve() => Resolve(Environment.GetEnvironmentVariable, File.Exists);

    internal static string Resolve(Func<string, string?> getVariable, Func<string, bool> exists)
    {
        string? shell = getVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(shell) && exists(shell))
        {
            return shell;
        }

        if (exists("/bin/zsh"))
        {
            return "/bin/zsh";
        }

        if (exists("/bin/bash"))
        {
            return "/bin/bash";
        }

        throw new InvalidOperationException("No shell found. Set SHELL, or install zsh or bash.");
    }
}
