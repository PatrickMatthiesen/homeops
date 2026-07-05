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

        var args = runner.BuildWslArguments(Path.Combine(root, "ansible", "site.yml"), Path.Combine(root, "vault.txt"), null, ["--check"]);

        Assert.Equal(["-d", "Ubuntu", "--", "bash", "-lc", ExpectedScript, "homeops", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/vault.txt", string.Empty, "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/site.yml", "-i", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/inventory.ini", "--check"], args);
    }

    [Fact]
    public void BuildsExpectedWslCommandWithBecomePasswordFile()
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

        var args = runner.BuildWslArguments(Path.Combine(root, "ansible", "site.yml"), Path.Combine(root, "vault.txt"), Path.Combine(root, "become.txt"), ["--check"]);

        Assert.Equal(["-d", "Ubuntu", "--", "bash", "-lc", ExpectedScript, "homeops", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/vault.txt", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/become.txt", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/site.yml", "-i", "/mnt/" + char.ToLowerInvariant(root[0]) + root[2..].Replace('\\', '/') + "/ansible/inventory.ini", "--check"], args);
    }

    private const string ExpectedScript = "set -e; export ANSIBLE_LOCAL_TEMP=/tmp/homeops-ansible; vault=\\$(mktemp /tmp/homeops-vault.XXXXXX); become=''; cleanup() { rm -f \"\\$vault\"; if [ -n \"\\$become\" ]; then rm -f \"\\$become\"; fi; }; trap cleanup EXIT; cp \"\\$1\" \"\\$vault\"; chmod 600 \"\\$vault\"; shift; become_args=(); if [ -n \"\\$1\" ]; then become=\\$(mktemp /tmp/homeops-become.XXXXXX); cp \"\\$1\" \"\\$become\"; chmod 600 \"\\$become\"; become_args=(--become --become-password-file \"\\$become\"); fi; shift; ansible-playbook \"\\$@\" --vault-password-file \"\\$vault\" \"\\${become_args[@]}\"";
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

    public Task<int> RunInteractiveAsync(InteractiveProcessRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
