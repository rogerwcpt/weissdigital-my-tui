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

    /// <summary>
    /// Creates one new folder named <paramref name="name"/> inside <paramref name="parent"/>.
    /// <paramref name="parent"/> may be the workspace root. An existing file or folder is left alone.
    /// </summary>
    internal static string CreateDirectory(string root, string parent, string name)
    {
        string? problem = NameProblem(name);
        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        string directory = RequireContainer(root, parent);
        string target = Path.Combine(directory, name);
        if (Path.Exists(target))
        {
            throw new IOException("A file or folder with that name already exists.");
        }

        Directory.CreateDirectory(target);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
    }

    /// <summary>
    /// Copies <paramref name="path"/> into <paramref name="destinationDirectory"/>.
    /// The destination stays inside the workspace and an existing name is left alone.
    /// </summary>
    internal static string Copy(string root, string path, string destinationDirectory) =>
        Place(root, path, destinationDirectory, move: false);

    /// <summary>
    /// Moves <paramref name="path"/> into <paramref name="destinationDirectory"/>.
    /// Moving an item into the folder that already holds it returns that same path.
    /// </summary>
    internal static string Move(string root, string path, string destinationDirectory) =>
        Place(root, path, destinationDirectory, move: true);

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

    private static string Place(string root, string path, string destinationDirectory, bool move)
    {
        string source = RequireChild(root, path);
        string destination = RequireContainer(root, destinationDirectory);
        string realSource = Workspace.Real(source);
        string realDestination = Workspace.Real(destination);
        string prefix = realSource.EndsWith(Path.DirectorySeparatorChar)
            ? realSource
            : realSource + Path.DirectorySeparatorChar;
        if (string.Equals(realDestination, realSource, Comparison)
            || realDestination.StartsWith(prefix, Comparison))
        {
            throw new IOException("Choose a folder that is not inside the selected item.");
        }

        string target = Path.Combine(destination, Path.GetFileName(source));
        if (string.Equals(Trim(source), Trim(target), Comparison))
        {
            return source;
        }

        if (Path.Exists(target))
        {
            throw new IOException("A file or folder with that name already exists.");
        }

        bool directory = File.GetAttributes(source).HasFlag(FileAttributes.Directory);
        if (move)
        {
            if (directory)
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }
        }
        else if (directory)
        {
            CopyTree(root, source, target);
        }
        else
        {
            File.Copy(source, target);
        }

        return Trim(Path.GetFullPath(target));
    }

    private static void CopyTree(string root, string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (FileNode child in Workspace.Children(root, source))
        {
            string childTarget = Path.Combine(target, child.Name);
            if (child.Directory)
            {
                CopyTree(root, child.Path, childTarget);
            }
            else
            {
                File.Copy(child.Path, childTarget);
            }
        }
    }

    private static string Trim(string path) =>
        path.Length > 1 ? path.TrimEnd(Path.DirectorySeparatorChar) : path;

    private static StringComparison Comparison =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string RequireContainer(string root, string path)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        bool rootItself = string.Equals(
            Workspace.Real(fullRoot),
            Workspace.Real(source),
            OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        if (!rootItself)
        {
            source = RequireChild(root, path);
        }
        else if (!Workspace.IsInside(fullRoot, source))
        {
            throw new IOException("The selected path is outside the session directory.");
        }

        if (!Directory.Exists(source))
        {
            throw new IOException("That folder is not available.");
        }

        return source;
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
