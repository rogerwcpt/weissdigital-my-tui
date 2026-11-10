using MyTui.UI;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace MyTui.Tests;

public class FolderDialogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("my-tui-folder").FullName;

    public FolderDialogTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
    }

    [Fact]
    public void RightEntersTheSelectedFolderWithoutAcceptingTheDialog()
    {
        using var dialog = Create(_root);
        var table = Table(dialog);
        Select(table, "alpha");

        Assert.True(table.NewKeyDownEvent(Key.CursorRight));

        Assert.Equal(Path.Combine(_root, "alpha"), dialog.Path);
        Assert.True(HasRow(table, "nested"));
        Assert.True(dialog.Canceled);
        Assert.True(table.HasFocus);
    }

    [Fact]
    public void LeftGoesUpFromTheCurrentFolderNotTheSelectedChild()
    {
        using var dialog = Create(Path.Combine(_root, "alpha"));
        var table = Table(dialog);
        Select(table, "nested");
        Assert.Equal(Path.Combine(_root, "alpha", "nested"), dialog.Path);

        Assert.True(table.NewKeyDownEvent(Key.CursorLeft));

        Assert.Equal(_root, dialog.Path);
        Assert.True(HasRow(table, "beta"));
        Assert.True(dialog.Canceled);
        Assert.True(table.HasFocus);
    }

    [Fact]
    public void LeftAtTheFilesystemRootDoesNothing()
    {
        string root = Path.GetPathRoot(_root)!;
        using var dialog = Create(root);
        var table = Table(dialog);

        Assert.True(table.NewKeyDownEvent(Key.CursorLeft));

        Assert.Equal(root, dialog.Path);
        Assert.True(dialog.Canceled);
    }

    [Fact]
    public void RightOnTheParentEntryNavigatesWithoutAcceptingTheDialog()
    {
        using var dialog = Create(Path.Combine(_root, "beta"));
        var table = Table(dialog);
        Assert.True(table.NewKeyDownEvent(Key.CursorRight));

        // The only entry is "..", so Right follows it just like Enter.
        Assert.Equal(_root, dialog.Path);
        Assert.True(dialog.Canceled);
    }

    [Fact]
    public void UpAndDownStillChangeTheSelectedRow()
    {
        using var dialog = Create(_root);
        var table = Table(dialog);
        Select(table, "alpha");
        int row = table.Value!.SelectedCell.Y;

        Assert.True(table.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(row + 1, table.Value!.SelectedCell.Y);
        Assert.True(table.NewKeyDownEvent(Key.CursorUp));
        Assert.Equal(row, table.Value!.SelectedCell.Y);
    }

    [Fact]
    public void LeftAndRightInThePathFieldStillMoveTheCursor()
    {
        using var dialog = Create(_root);
        var path = dialog.SubViews.OfType<TextField>().Single(field => field.Text == _root);
        path.SetFocus();
        path.MoveEnd();
        int end = path.InsertionPoint;

        Assert.True(path.NewKeyDownEvent(Key.CursorLeft));
        Assert.Equal(end - 1, path.InsertionPoint);
        Assert.True(path.NewKeyDownEvent(Key.CursorRight));
        Assert.Equal(end, path.InsertionPoint);
        Assert.Equal(_root, dialog.Path);
    }

    private static OpenDialog Create(string start)
    {
        var dialog = FolderDialog.Create(start);
        dialog.BeginInit();
        dialog.EndInit();
        Table(dialog).SetFocus();
        return dialog;
    }

    private static TableView Table(OpenDialog dialog) =>
        dialog.SubViews.SelectMany(view => view.SubViews).OfType<TableView>().Single();

    private static bool HasRow(TableView table, string name) =>
        Enumerable.Range(0, table.Table!.Rows)
            .Any(row => table.Table[row, 0].ToString()!.EndsWith(name, StringComparison.Ordinal));

    private static void Select(TableView table, string name)
    {
        int row = Enumerable.Range(0, table.Table!.Rows)
            .Single(row => table.Table[row, 0].ToString()!.EndsWith(name, StringComparison.Ordinal));
        table.SetSelection(0, row, false);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
