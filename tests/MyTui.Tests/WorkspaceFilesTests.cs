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

    [Fact]
    public void CreateDirectoryMakesOneFolderInsideTheWorkspace()
    {
        string created = WorkspaceFiles.CreateDirectory(Root, Root, "fresh");
        Assert.Equal(Path.Combine(Root, "fresh"), created);
        Assert.True(Directory.Exists(created));

        string parent = Directory.CreateDirectory(Path.Combine(Root, "parent")).FullName;
        string child = WorkspaceFiles.CreateDirectory(Root, parent, "child");
        Assert.Equal(Path.Combine(parent, "child"), child);
        Assert.True(Directory.Exists(child));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateDirectoryDoesNotOverwrite(bool directory)
    {
        string target = Path.Combine(Root, "taken");
        if (directory)
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep me");
        }
        else
        {
            File.WriteAllText(target, "keep me");
        }

        Assert.Throws<IOException>(() => WorkspaceFiles.CreateDirectory(Root, Root, "taken"));
        Assert.Equal("keep me", File.ReadAllText(directory ? Path.Combine(target, "keep.txt") : target));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("nested/file")]
    public void CreateDirectoryRejectsPathsAndInvalidNames(string name)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceFiles.CreateDirectory(Root, Root, name));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    [Fact]
    public void CreateDirectoryCannotLeaveTheWorkspaceThroughALink()
    {
        string outside = Directory.CreateDirectory(Path.Combine(_parent, "outside")).FullName;
        string link = Path.Combine(Root, "outside-link");
        Directory.CreateSymbolicLink(link, outside);
        Assert.Throws<IOException>(() => WorkspaceFiles.CreateDirectory(Root, link, "fresh"));
        Assert.Throws<IOException>(() => WorkspaceFiles.CreateDirectory(Root, outside, "fresh"));
        Assert.False(Directory.Exists(Path.Combine(outside, "fresh")));
        Assert.False(Path.Exists(Path.Combine(Root, "fresh")));
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

    [Fact]
    public void CopyAndMoveStayInsideTheWorkspaceAndDoNotOverwrite()
    {
        string folder = Directory.CreateDirectory(Path.Combine(Root, "folder")).FullName;
        File.WriteAllText(Path.Combine(folder, "keep.txt"), "keep me");
        string source = Path.Combine(Root, "notes.txt");
        File.WriteAllText(source, "preview text");
        string taken = Path.Combine(folder, "notes.txt");
        File.WriteAllText(taken, "already here");

        Assert.Throws<IOException>(() => WorkspaceFiles.Copy(Root, source, folder));
        Assert.Equal("already here", File.ReadAllText(taken));
        Assert.Equal("preview text", File.ReadAllText(source));

        File.Delete(taken);
        string copied = WorkspaceFiles.Copy(Root, source, folder);
        Assert.Equal(Path.Combine(folder, "notes.txt"), copied);
        Assert.Equal("preview text", File.ReadAllText(copied));
        Assert.True(File.Exists(source));

        string parked = Directory.CreateDirectory(Path.Combine(Root, "parked")).FullName;
        string moved = WorkspaceFiles.Move(Root, copied, parked);
        Assert.Equal(Path.Combine(parked, "notes.txt"), moved);
        Assert.False(File.Exists(copied));
        Assert.Equal("preview text", File.ReadAllText(moved));
        Assert.Equal(moved, WorkspaceFiles.Move(Root, moved, parked));

        string copiedFolder = WorkspaceFiles.Copy(Root, folder, parked);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(copiedFolder, "keep.txt")));
        Assert.True(Directory.Exists(folder));
        string elsewhere = Directory.CreateDirectory(Path.Combine(Root, "elsewhere")).FullName;
        string movedFolder = WorkspaceFiles.Move(Root, copiedFolder, elsewhere);
        Assert.Equal(Path.Combine(elsewhere, "folder"), movedFolder);
        Assert.False(Directory.Exists(copiedFolder));

        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        Assert.Throws<IOException>(() => WorkspaceFiles.Copy(Root, folder, Path.Combine(folder, "inner")));
        string outside = Directory.CreateDirectory(Path.Combine(_parent, "outside")).FullName;
        Assert.Throws<IOException>(() => WorkspaceFiles.Move(Root, source, outside));
        Assert.Throws<IOException>(() => WorkspaceFiles.Copy(Root, source, outside));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void CopySkipsALinkThatLeavesTheWorkspace()
    {
        string outside = Directory.CreateDirectory(Path.Combine(_parent, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "safe");
        string folder = Directory.CreateDirectory(Path.Combine(Root, "folder")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(folder, "link"), outside);
        string parked = Directory.CreateDirectory(Path.Combine(Root, "parked")).FullName;

        string copied = WorkspaceFiles.Copy(Root, folder, parked);
        Assert.False(Path.Exists(Path.Combine(copied, "link")));
        Assert.Equal("safe", File.ReadAllText(Path.Combine(outside, "secret.txt")));
        Assert.Throws<IOException>(() => WorkspaceFiles.Move(Root, Path.Combine(folder, "link"), parked));
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);
}
