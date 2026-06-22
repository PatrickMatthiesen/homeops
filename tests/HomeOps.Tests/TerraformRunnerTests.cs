using HomeOps.Cli;
using HomeOps.Cli.Audit;
using HomeOps.Cli.Configuration;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Runners;

namespace HomeOps.Tests;

public sealed class TerraformRunnerTests
{
    [Fact]
    public async Task PlanInjectsPublicHalfOfConfiguredDeployKey()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "terraform", "demo-app");
        Directory.CreateDirectory(target);
        var privateKeyPath = Path.Combine(root, "deploy-key");
        var publicKey = "ssh-ed25519 AQID synthetic@test";
        await File.WriteAllTextAsync(privateKeyPath + ".pub", publicKey);

        try
        {
            var processes = new CapturingProcessRunner();
            var credentials = new TerraformCredentialStore(privateKeyPath);
            var config = new HomeOpsConfig { InfrastructureRepo = root };
            var paths = new PathResolver(config);
            var services = new AppServices(config, paths, credentials, processes, new GitInfo(processes), new AuditWriter(paths));

            var result = await new TerraformRunner(services).PlanAsync("demo-app", json: false, writePlan: false);

            Assert.Equal(0, result.ExitCode);
            var terraform = Assert.Single(processes.Requests, request => request.FileName == "terraform");
            Assert.Equal(publicKey, terraform.Environment["TF_VAR_ssh_public_key"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanDoesNotFallBackToReadingConfiguredPrivateKey()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "terraform", "demo-app");
        Directory.CreateDirectory(target);
        var privateKeyPath = Path.Combine(root, "deploy-key");
        await File.WriteAllTextAsync(privateKeyPath, "synthetic private material");

        try
        {
            var processes = new CapturingProcessRunner();
            var credentials = new TerraformCredentialStore(privateKeyPath);
            var config = new HomeOpsConfig { InfrastructureRepo = root };
            var paths = new PathResolver(config);
            var services = new AppServices(config, paths, credentials, processes, new GitInfo(processes), new AuditWriter(paths));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new TerraformRunner(services).PlanAsync("demo-app", json: false, writePlan: false));

            Assert.Contains("public key file was not found", exception.Message);
            Assert.DoesNotContain(processes.Requests, request => request.FileName == "terraform");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TerraformCredentialStore(string deployKeyPath) : ICredentialStore
    {
        public string? Get(string name) => name switch
        {
            CredentialKeys.SshDeployKeyPath => deployKeyPath,
            CredentialKeys.ProxmoxEndpoint => "https://proxmox.example:8006",
            CredentialKeys.ProxmoxTerraformToken => "synthetic-token",
            _ => null
        };

        public void Set(string name, string secret) { }
        public void Delete(string name) { }
        public IReadOnlyDictionary<string, bool> ListMetadata(IEnumerable<string> names) =>
            names.ToDictionary(name => name, _ => true);
    }

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }

        public Task<int> RunInteractiveAsync(InteractiveProcessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
