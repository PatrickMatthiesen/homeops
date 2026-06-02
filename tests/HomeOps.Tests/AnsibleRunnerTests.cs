using HomeOps.Cli;
using HomeOps.Cli.Audit;
using HomeOps.Cli.Configuration;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Runners;

namespace HomeOps.Tests;

public sealed class AnsibleRunnerTests
{
    [Fact]
    public void BuildsExpectedWslCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var config = new HomeOpsConfig
        {
            InfrastructureRepo = root,
            Ansible = new AnsibleConfig
            {
                PlaybooksRoot = "ansible",
                InventoryPath = "ansible/inventory.ini",
                WslDistro = "Ubuntu"
            }
        };
        var paths = new PathResolver(config);
        var services = new AppServices(config, paths, new FakeCredentialStore(), new FakeProcessRunner(), new GitInfo(new FakeProcessRunner()), new AuditWriter(paths));
        var runner = new AnsibleRunner(services);

        var args = runner.BuildWslArguments(Path.Combine(root, "ansible", "site.yml"), Path.Combine(root, "vault.txt"), ["--check"]);

        Assert.Equal(["-d", "Ubuntu", "ansible-playbook", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/site.yml", "-i", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/inventory.ini", "--vault-password-file", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/vault.txt", "--check"], args);
    }
}

internal sealed class FakeCredentialStore : ICredentialStore
{
    public string? Get(string name) => name;
    public void Set(string name, string secret) { }
    public void Delete(string name) { }
    public IReadOnlyDictionary<string, bool> ListMetadata(IEnumerable<string> names) => names.ToDictionary(name => name, _ => true);
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }
}
