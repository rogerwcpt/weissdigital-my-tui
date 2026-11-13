namespace MyTui.Sessions;

/// <summary>File-panel mutations: only children of the workspace, never its root or an overwrite.</summary>
internal static class WorkspaceFiles
{
    internal static string? NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is empty.";
        }

        return name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')
            ? "Use a single file or folder name, not a path."
            : null;
    }

    internal static string Rename(string root, string path, string name)
    {
        string? problem = NameProblem(name);
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        string source = RequireChild(root, path);
        string target = Path.Combine(Path.GetDirectoryName(source)!, name);
        if (source == target)
        {
            return source;
        }

        // Neither operation overwrites an existing destination.
        if (File.GetAttributes(source).HasFlag(FileAttributes.Directory))
        {
            Directory.Move(source, target);
        }
        else
        {
            File.Move(source, target);
        }

        return target;
    }

    internal static void Delete(string root, string path)
    {
        string source = RequireChild(root, path);
        if (File.GetAttributes(source).HasFlag(FileAttributes.Directory))
        {
            // Directory.Delete removes directory links themselves, not their targets.
            Directory.Delete(source, recursive: true);
        }
        else
        {
            File.Delete(source);
        }
    }

    private static string RequireChild(string root, string path)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string relative = Path.GetRelativePath(fullRoot, source);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("Select a file or folder inside the session directory.");
        }

        // Check each ancestor as well: resolving just the leaf misses links in its parent path.
        for (string? current = source; current is not null && current != fullRoot;
             current = Path.GetDirectoryName(current))
        {
            if (!Workspace.IsInside(fullRoot, current))
            {
                throw new IOException("The selected path is outside the session directory.");
            }
        }

        return source;
    }
}
