using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Risk;

namespace HomeOps.Cli.Runners;

public sealed class AnsibleRunner(AppServices services)
{
    public Task<CommandResult> SyntaxAsync(string playbook) => RunAsync(playbook, ["--syntax-check"], null, false, "ansible.syntax");

    public Task<CommandResult> CheckAsync(string playbook, string? limit)
    {
        limit ??= services.Paths.ResolveAnsibleDefaultLimit(playbook);
        var args = new List<string> { "--check" };
        if (!string.IsNullOrWhiteSpace(limit))
        {
            args.Add("--limit");
            args.Add(limit);
        }

        return RunAsync(playbook, args, limit, false, "ansible.check");
    }

    public Task<CommandResult> ApplyAsync(string playbook, string? limit, bool yes)
    {
        limit ??= services.Paths.ResolveAnsibleDefaultLimit(playbook);
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(limit))
        {
            args.Add("--limit");
            args.Add(limit);
        }

        return RunAsync(playbook, args, limit, true, "ansible.apply", yes);
    }

    private async Task<CommandResult> RunAsync(string playbook, IReadOnlyList<string> modeArgs, string? limit, bool apply, string category, bool yes = false)
    {
        var playbookPath = services.Paths.ResolveAnsiblePlaybook(playbook);
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);
        var vaultPassword = services.Credentials.Get(CredentialKeys.AnsibleVaultPassword) ?? throw new InvalidOperationException("Missing ansible.vault_password.");
        var redactor = RunnerHelpers.BuildRedactor(services.Credentials);
        var tempFile = Path.Combine(Path.GetTempPath(), $"homeops-vault-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, vaultPassword);
            var args = BuildWslArguments(playbookPath, tempFile, modeArgs);
            var process = await services.Processes.RunAsync(new("wsl.exe", args, services.Paths.RepoRoot, new Dictionary<string, string?>()));
            var risk = apply ? RiskDetector.AnsibleApply(git.IsDirty, limit, unexpectedPath: false) : "normal";
            var result = RunnerHelpers.ToCommandResult(process, redactor, category, playbook, risk, null, apply && !yes);
            var auditId = services.Audit.Write(RunnerHelpers.CreateAudit(category, playbook, services.Paths.RepoRoot, git, result.ExitCode, risk, yes ? "--yes" : "none", result.Summary));
            return result with { AuditEventId = auditId };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public IReadOnlyList<string> BuildWslArguments(string playbookPath, string vaultPasswordFile, IReadOnlyList<string> modeArgs)
    {
        var args = new List<string>
        {
            "-d",
            services.Config.Ansible.WslDistro,
            "ansible-playbook",
            ToWslPath(playbookPath),
            "-i",
            ToWslPath(services.Paths.InventoryPath),
            "--vault-password-file",
            ToWslPath(vaultPasswordFile)
        };
        args.AddRange(modeArgs);
        return args;
    }

    private static string ToWslPath(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath);
        var drive = char.ToLowerInvariant(full[0]);
        var rest = full[2..].Replace('\\', '/');
        return $"/mnt/{drive}{rest}";
    }
}
