namespace MyTui.Sessions;

internal sealed record FileNode(string Path, string Name, bool Directory)
{
    public override string ToString() => Directory ? Name + "/" : Name;
}

/// <summary>
/// The file tree is rooted at the session cwd. A path is inside only when its real path is that root or a child of it.
/// </summary>
internal static class Workspace
{
    internal static bool IsInside(string root, string candidate)
    {
        string realRoot = Real(root);
        string realCandidate;
        try
        {
            realCandidate = Real(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        if (string.Equals(realRoot, realCandidate, Comparison))
        {
            return true;
        }

        string prefix = realRoot.EndsWith(Path.DirectorySeparatorChar)
            ? realRoot
            : realRoot + Path.DirectorySeparatorChar;
        return realCandidate.StartsWith(prefix, Comparison);
    }

    internal static IReadOnlyList<FileNode> Children(string root, string directory)
    {
        if (!IsInside(root, directory) || !Directory.Exists(directory))
        {
            return [];
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<FileNode>();
        foreach (string entry in entries)
        {
            string name = Path.GetFileName(entry);
            if (name is "" or "." or "..")
            {
                continue;
            }

            if (!IsInside(root, entry))
            {
                continue;
            }

            found.Add(new FileNode(Path.GetFullPath(entry), name, Directory.Exists(entry)));
        }

        found.Sort((left, right) =>
        {
            int kind = right.Directory.CompareTo(left.Directory);
            return kind != 0
                ? kind
                : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return found;
    }

    internal static string Real(string path)
    {
        string full = Path.GetFullPath(path);
        try
        {
            FileSystemInfo? link = File.ResolveLinkTarget(full, returnFinalTarget: true);
            if (link is null)
            {
                return Trim(full);
            }

            string target = link.FullName;
            if (!Path.IsPathRooted(target))
            {
                string? parent = Path.GetDirectoryName(full);
                target = parent is null ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(parent, target));
            }

            return Trim(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Trim(full);
        }
    }

    private static string Trim(string path)
    {
        if (path.Length > 1)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar);
        }

        return path;
    }

    private static StringComparison Comparison =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
