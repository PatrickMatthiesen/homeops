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

    [Fact]
    public void ResolvesConfiguredTerraformTargetPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resolver = new PathResolver(new HomeOpsConfig
        {
            InfrastructureRepo = root,
            Terraform = new TerraformConfig
            {
                Targets =
                {
                    ["sample-app"] = new TerraformTargetConfig { Path = "terraform/sample-app" }
                }
            }
        });

        Assert.Equal(Path.Combine(root, "terraform", "sample-app"), resolver.ResolveTerraformTarget("sample-app"));
        Assert.Equal(Path.Combine(root, "terraform", "worker-node"), resolver.ResolveTerraformTarget("worker-node"));
    }

    [Fact]
    public void ResolvesConfiguredAnsiblePlaybookPathsAndDefaultLimits()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var resolver = new PathResolver(new HomeOpsConfig
        {
            InfrastructureRepo = root,
            Ansible = new AnsibleConfig
            {
                PlaybooksRoot = "playbooks",
                Playbooks =
                {
                    ["sample-app"] = new AnsiblePlaybookConfig
                    {
                        Path = "playbooks/sample-app.yml",
                        DefaultLimit = "app.example.test"
                    }
                }
            }
        });

        Assert.Equal(Path.Combine(root, "playbooks", "sample-app.yml"), resolver.ResolveAnsiblePlaybook("sample-app"));
        Assert.Equal(Path.Combine(root, "playbooks", "site.yml"), resolver.ResolveAnsiblePlaybook("site.yml"));
        Assert.Equal("app.example.test", resolver.ResolveAnsibleDefaultLimit("sample-app"));
    }
}
