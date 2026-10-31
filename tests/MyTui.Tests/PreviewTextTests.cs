using MyTui.Sessions;

namespace MyTui.Tests;

public class PreviewTextTests
{
    [Fact]
    public void MarkdownPlainAndBinaryFollowThePreviewRules()
    {
        string directory = Directory.CreateTempSubdirectory("my-tui-preview").FullName;
        string markdown = Path.Combine(directory, "note.md");
        string plain = Path.Combine(directory, "note.txt");
        string binary = Path.Combine(directory, "blob.bin");
        File.WriteAllText(markdown, "# Title\n\n- item\n");
        File.WriteAllText(plain, "hello");
        File.WriteAllBytes(binary, [0x00, 0x01, 0x02]);
        try
        {
            PreviewText.Content md = PreviewText.Read(markdown);
            PreviewText.Content text = PreviewText.Read(plain);
            PreviewText.Content blob = PreviewText.Read(binary);
            PreviewText.Content folder = PreviewText.Read(directory);
            Assert.Equal(PreviewText.Kind.Markdown, md.Kind);
            Assert.Contains("# Title", md.Text);
            Assert.Equal(PreviewText.Kind.Text, text.Kind);
            Assert.Equal("hello", text.Text);
            Assert.Equal(PreviewText.Kind.Binary, blob.Kind);
            Assert.Equal("Binary file. No preview.", blob.Text);
            Assert.Equal(PreviewText.Kind.Directory, folder.Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}