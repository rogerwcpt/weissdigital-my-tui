using MyTui.Terminal;

namespace MyTui.Tests;

public class ShellLocatorTests
{
    [Fact]
    public void UsesShellWhenItExists()
    {
        string resolved = ShellLocator.Resolve(_ => "/custom/sh", path => path == "/custom/sh");

        Assert.Equal("/custom/sh", resolved);
    }

    [Fact]
    public void SkipsMissingShellForZsh()
    {
        string resolved = ShellLocator.Resolve(_ => "/missing", path => path == "/bin/zsh");

        Assert.Equal("/bin/zsh", resolved);
    }

    [Fact]
    public void FallsThroughToBash()
    {
        string resolved = ShellLocator.Resolve(_ => null, path => path == "/bin/bash");

        Assert.Equal("/bin/bash", resolved);
    }

    [Fact]
    public void ThrowsWhenNothingExists()
    {
        Assert.Throws<InvalidOperationException>(() => ShellLocator.Resolve(_ => " ", _ => false));
    }
}
