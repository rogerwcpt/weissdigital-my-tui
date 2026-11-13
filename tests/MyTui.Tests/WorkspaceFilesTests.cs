using MyTui.Sessions;

namespace MyTui.Tests;

public sealed class WorkspaceFilesTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("my-tui-file-actions").FullName;
    private string Root => Path.Combine(_parent, "workspace");

    public WorkspaceFilesTests() => Directory.CreateDirectory(Root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilesAndFoldersCanBeRenamedAndDeleted(bool directory)
    {
        string source = Path.Combine(Root, "before");
        if (directory)
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "contents.txt"), "keep me");
        }
        else
        {
            File.WriteAllText(source, "keep me");
        }

        string target = WorkspaceFiles.Rename(Root, source, "after");
        Assert.Equal(Path.Combine(Root, "after"), target);
        Assert.False(Path.Exists(source));
        Assert.Equal("keep me", File.ReadAllText(directory ? Path.Combine(target, "contents.txt") : target));
        Assert.Equal(target, WorkspaceFiles.Rename(Root, target, "after"));

        WorkspaceFiles.Delete(Root, target);
        Assert.False(Path.Exists(target));
        Assert.True(Directory.Exists(Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenameDoesNotOverwrite(bool directory)
    {
        string source = Path.Combine(Root, "source");
        string target = Path.Combine(Root, "target");
        if (directory)
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
        }
        else
        {
            File.WriteAllText(source, "source contents");
            File.WriteAllText(target, "target contents");
        }

        Assert.ThrowsAny<IOException>(() => WorkspaceFiles.Rename(Root, source, "target"));
        Assert.True(Path.Exists(source));
        Assert.True(Path.Exists(target));
        if (!directory)
        {
            Assert.Equal("target contents", File.ReadAllText(target));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("nested/file")]
    [InlineData("nested\\file")]
    [InlineData("/absolute")]
    [InlineData("bad\0name")]
    public void RenameRejectsPathsAndInvalidNames(string name)
    {
        string source = Path.Combine(Root, "safe.txt");
        File.WriteAllText(source, "safe");
        Assert.NotNull(WorkspaceFiles.NameProblem(name));
        Assert.Throws<ArgumentException>(() => WorkspaceFiles.Rename(Root, source, name));
        Assert.Equal("safe", File.ReadAllText(source));
    }

    [Fact]
    public void WorkspaceRootAndOutsidePathsCannotBeChanged()
    {
        string outside = Path.Combine(_parent, "outside.txt");
        File.WriteAllText(outside, "safe");
        foreach (string path in new[] { Root, outside, Path.Combine(Root, "..", "outside.txt") })
        {
            Assert.Throws<IOException>(() => WorkspaceFiles.Rename(Root, path, "renamed"));
            Assert.Throws<IOException>(() => WorkspaceFiles.Delete(Root, path));
        }

        Assert.True(Directory.Exists(Root));
        Assert.Equal("safe", File.ReadAllText(outside));
    }

    [Fact]
    public void LinksCannotEscapeTheWorkspaceAndRecursiveDeleteDoesNotFollowLinks()
    {
        string outside = Directory.CreateDirectory(Path.Combine(_parent, "outside")).FullName;
        string secret = Path.Combine(outside, "secret.txt");
        File.WriteAllText(secret, "safe");
        string link = Path.Combine(Root, "outside-link");
        Directory.CreateSymbolicLink(link, outside);
        foreach (string path in new[] { link, Path.Combine(link, "secret.txt") })
        {
            Assert.Throws<IOException>(() => WorkspaceFiles.Rename(Root, path, "renamed"));
            Assert.Throws<IOException>(() => WorkspaceFiles.Delete(Root, path));
        }

        string folder = Directory.CreateDirectory(Path.Combine(Root, "folder")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside);
        WorkspaceFiles.Delete(Root, folder);
        Assert.False(Directory.Exists(folder));
        Assert.Equal("safe", File.ReadAllText(secret));
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);
}
