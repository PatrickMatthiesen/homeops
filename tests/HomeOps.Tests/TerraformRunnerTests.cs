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

    [Fact]
    public async Task PlanSummaryHighlightsActionsAndChangedValues()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "terraform", "web-panel");
        Directory.CreateDirectory(target);
        var privateKeyPath = Path.Combine(root, "deploy-key");
        await File.WriteAllTextAsync(privateKeyPath + ".pub", "ssh-ed25519 AQID synthetic@test");

        try
        {
            var processes = new CapturingProcessRunner
            {
                Output = """
                    Terraform will perform the following actions:

                      # proxmox_virtual_environment_vm.panel will be updated in-place
                      ~ resource "proxmox_virtual_environment_vm" "panel" {
                            id = "112"
                            # (33 unchanged attributes hidden)

                          ~ memory {
                              ~ dedicated      = 4096 -> 3072
                                # (4 unchanged attributes hidden)
                            }
                        }

                    Plan: 0 to add, 1 to change, 0 to destroy.
                    """
            };
            var credentials = new TerraformCredentialStore(privateKeyPath);
            var config = new HomeOpsConfig { InfrastructureRepo = root };
            var paths = new PathResolver(config);
            var services = new AppServices(config, paths, credentials, processes, new GitInfo(processes), new AuditWriter(paths));

            var result = await new TerraformRunner(services).PlanAsync("web-panel", json: false, writePlan: false);

            Assert.Equal(
                """
                Plan: 0 to add, 1 to change, 0 to destroy.
                proxmox_virtual_environment_vm.panel: updated in-place
                dedicated: 4096 -> 3072
                Plan not saved. Re-run with --out before apply to guarantee the exact actions.
                """.ReplaceLineEndings(),
                result.Summary.ReplaceLineEndings());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanSummaryIncludesSavedPlanIdWhenOutIsUsed()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "terraform", "web-panel");
        Directory.CreateDirectory(target);
        var privateKeyPath = Path.Combine(root, "deploy-key");
        await File.WriteAllTextAsync(privateKeyPath + ".pub", "ssh-ed25519 AQID synthetic@test");

        try
        {
            var processes = new CapturingProcessRunner
            {
                Output = "Plan: 0 to add, 1 to change, 0 to destroy."
            };
            var credentials = new TerraformCredentialStore(privateKeyPath);
            var config = new HomeOpsConfig { InfrastructureRepo = root };
            var paths = new PathResolver(config);
            var services = new AppServices(config, paths, credentials, processes, new GitInfo(processes), new AuditWriter(paths));

            var result = await new TerraformRunner(services).PlanAsync("web-panel", json: false, writePlan: true);

            Assert.Contains("planId=", result.Subject);
            Assert.Contains("Saved plan: ", result.Summary);
            Assert.Contains(result.Subject.Split("planId=", StringSplitOptions.None)[1], result.Summary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplySummaryStripsAnsiAndHighlightsCompletion()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "terraform", "web-panel");
        Directory.CreateDirectory(target);
        var privateKeyPath = Path.Combine(root, "deploy-key");
        await File.WriteAllTextAsync(privateKeyPath + ".pub", "ssh-ed25519 AQID synthetic@test");

        try
        {
            var processes = new CapturingProcessRunner
            {
                Output = "\u001b[0m\u001b[1m\u001b[32mApply complete! Resources: 0 added, 1 changed, 0 destroyed.\u001b[0m\n\n\u001b[0m\u001b[1m\u001b[32mOutputs:\u001b[0m\npanel_command = \"homeops ansible apply web-panel --limit web-panel.example\"\npanel_vm_id = 112\npanel_vm_ip = \"192.168.1.178\"\n"
            };
            var credentials = new TerraformCredentialStore(privateKeyPath);
            var config = new HomeOpsConfig { InfrastructureRepo = root };
            var paths = new PathResolver(config);
            var services = new AppServices(config, paths, credentials, processes, new GitInfo(processes), new AuditWriter(paths));

            var result = await new TerraformRunner(services).ApplyAsync("web-panel", planId: null, yes: true);

            Assert.Equal(
                """
                Apply complete! Resources: 0 added, 1 changed, 0 destroyed.
                Outputs:
                panel_command = "homeops ansible apply web-panel --limit web-panel.example"
                panel_vm_id = 112
                panel_vm_ip = "192.168.1.178"
                """.ReplaceLineEndings(),
                result.Summary.ReplaceLineEndings());
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
        public string Output { get; init; } = string.Empty;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(0, Output, string.Empty));
        }

        public Task<int> RunInteractiveAsync(InteractiveProcessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
