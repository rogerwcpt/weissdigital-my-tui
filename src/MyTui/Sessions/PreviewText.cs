using System.Text;

namespace MyTui.Sessions;

internal static class PreviewText
{
    internal const int MaxBytes = 256 * 1024;

    internal enum Kind
    {
        None,
        Markdown,
        Text,
        Binary,
        Directory,
    }

    internal readonly record struct Content(Kind Kind, string Text);

    internal static Content Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new Content(Kind.None, "Select a file.");
        }

        if (Directory.Exists(path) && !File.Exists(path))
        {
            return new Content(Kind.Directory, "Directory.");
        }

        if (!File.Exists(path))
        {
            return new Content(Kind.None, "File not found.");
        }

        var buffer = new byte[MaxBytes + 1];
        int read;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.Read(buffer);
        }
        catch (UnauthorizedAccessException)
        {
            return new Content(Kind.None, "Cannot read this file.");
        }
        catch (IOException ex)
        {
            return new Content(Kind.None, ex.Message);
        }

        int length = Math.Min(read, MaxBytes);
        for (int i = 0; i < length; i++)
        {
            if (buffer[i] == 0)
            {
                return new Content(Kind.Binary, "Binary file. No preview.");
            }
        }

        string text = Encoding.UTF8.GetString(buffer, 0, length);
        if (read > MaxBytes)
        {
            text += "\n\n[truncated]";
        }

        bool markdown = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        return new Content(markdown ? Kind.Markdown : Kind.Text, text);
    }
}
