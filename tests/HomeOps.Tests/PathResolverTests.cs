using HomeOps.Cli.Configuration;

namespace HomeOps.Tests;

public sealed class PathResolverTests
{
    [Fact]
    public void AllowsRelativePathUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resolved = PathResolver.ResolveUnderRoot(root, "target", "target");
        Assert.Equal(Path.Combine(root, "target"), resolved);
    }

    [Fact]
    public void RejectsTraversalOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "root");
        Assert.Throws<InvalidOperationException>(() => PathResolver.ResolveUnderRoot(root, "..", "target"));
    }
}
