using MyTui.Sessions;

namespace MyTui.Tests;

public class WorkspaceTests
{
    [Fact]
    public void ChildrenStayInsideTheSessionDirectory()
    {
        string parent = Directory.CreateTempSubdirectory("my-tui-tree").FullName;
        string root = Path.Combine(parent, "root");
        string sibling = Path.Combine(parent, "sibling");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "readme.md"), "hello");
        File.WriteAllText(Path.Combine(root, "README.md"), "top");
        Directory.CreateDirectory(sibling);
        string secret = Path.Combine(sibling, "secret.txt");
        File.WriteAllText(secret, "nope");
        string link = Path.Combine(root, "outside");
        File.CreateSymbolicLink(link, secret);

        try
        {
            Assert.True(Workspace.IsInside(root, Path.Combine(root, "docs", "readme.md")));
            Assert.True(Workspace.IsInside(root, Path.Combine(root, "docs", "..", "README.md")));
            Assert.False(Workspace.IsInside(root, Path.Combine(root, "..")));
            Assert.False(Workspace.IsInside(root, Path.Combine(root, "docs", "..", "..", "sibling")));
            Assert.False(Workspace.IsInside(root, link));
            Assert.Empty(Workspace.Children(root, Path.Combine(root, "..")));

            IReadOnlyList<FileNode> children = Workspace.Children(root, root);
            Assert.Equal(["docs/", "README.md"], children.Select(node => node.ToString()).ToArray());
            Assert.DoesNotContain(children, node => node.Name is ".." or "outside");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
