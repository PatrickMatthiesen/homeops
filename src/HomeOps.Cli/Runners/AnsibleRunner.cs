using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Risk;

namespace HomeOps.Cli.Runners;

public sealed class AnsibleRunner(AppServices services)
{
    public Task<CommandResult> SyntaxAsync(string playbook) => RunAsync(playbook, ["--syntax-check"], null, false, "ansible.syntax");

    public Task<CommandResult> CheckAsync(string playbook, string? limit, bool become = false)
    {
        limit ??= services.Paths.ResolveAnsibleDefaultLimit(playbook);
        var args = new List<string> { "--check" };
        if (!string.IsNullOrWhiteSpace(limit))
        {
            args.Add("--limit");
            args.Add(limit);
        }

        return RunAsync(playbook, args, limit, false, "ansible.check", become: become);
    }

    public Task<CommandResult> ApplyAsync(string playbook, string? limit, bool become, bool yes)
    {
        limit ??= services.Paths.ResolveAnsibleDefaultLimit(playbook);
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(limit))
        {
            args.Add("--limit");
            args.Add(limit);
        }

        return RunAsync(playbook, args, limit, true, "ansible.apply", yes, become);
    }

    private async Task<CommandResult> RunAsync(string playbook, IReadOnlyList<string> modeArgs, string? limit, bool apply, string category, bool yes = false, bool become = false)
    {
        var playbookPath = services.Paths.ResolveAnsiblePlaybook(playbook);
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);
        var vaultPassword = services.Credentials.Get(CredentialKeys.AnsibleVaultPassword) ?? throw new InvalidOperationException("Missing ansible.vault_password.");
        var becomePassword = become
            ? services.Credentials.Get(CredentialKeys.AnsibleBecomePassword) ?? throw new InvalidOperationException("Missing ansible.become_password. Run homeops login from a trusted local terminal.")
            : null;
        var redactor = RunnerHelpers.BuildRedactor(services.Credentials);
        var tempFile = Path.Combine(Path.GetTempPath(), $"homeops-vault-{Guid.NewGuid():N}.txt");
        var becomeTempFile = become ? Path.Combine(Path.GetTempPath(), $"homeops-become-{Guid.NewGuid():N}.txt") : null;
        try
        {
            File.WriteAllText(tempFile, vaultPassword);
            if (becomeTempFile is not null && becomePassword is not null)
            {
                File.WriteAllText(becomeTempFile, becomePassword);
            }

            var args = BuildWslArguments(playbookPath, tempFile, becomeTempFile, modeArgs);
            var process = await services.Processes.RunAsync(new("wsl.exe", args, services.Paths.RepoRoot, new Dictionary<string, string?>()));
            var risk = apply ? RiskDetector.AnsibleApply(git.IsDirty, limit, unexpectedPath: false) : "normal";
            var result = RunnerHelpers.ToCommandResult(process, redactor, category, playbook, risk, null, apply && !yes);
            var confirmationMode = yes ? "--yes" : "none";
            if (become)
            {
                confirmationMode = $"{confirmationMode};--become";
            }

            var auditId = services.Audit.Write(RunnerHelpers.CreateAudit(category, playbook, services.Paths.RepoRoot, git, result.ExitCode, risk, confirmationMode, result.Summary));
            return result with { AuditEventId = auditId };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }

            if (becomeTempFile is not null && File.Exists(becomeTempFile))
            {
                File.Delete(becomeTempFile);
            }
        }
    }

    public IReadOnlyList<string> BuildWslArguments(string playbookPath, string vaultPasswordFile, string? becomePasswordFile, IReadOnlyList<string> modeArgs)
    {
        var args = new List<string>
        {
            "-d",
            services.Config.Ansible.WslDistro,
            "--",
            "bash",
            "-lc",
            "set -e; export ANSIBLE_LOCAL_TEMP=/tmp/homeops-ansible; vault=\\$(mktemp /tmp/homeops-vault.XXXXXX); become=''; cleanup() { rm -f \"\\$vault\"; if [ -n \"\\$become\" ]; then rm -f \"\\$become\"; fi; }; trap cleanup EXIT; cp \"\\$1\" \"\\$vault\"; chmod 600 \"\\$vault\"; shift; become_args=(); if [ -n \"\\$1\" ]; then become=\\$(mktemp /tmp/homeops-become.XXXXXX); cp \"\\$1\" \"\\$become\"; chmod 600 \"\\$become\"; become_args=(--become --become-password-file \"\\$become\"); fi; shift; ansible-playbook \"\\$@\" --vault-password-file \"\\$vault\" \"\\${become_args[@]}\"",
            "homeops",
            ToWslPath(vaultPasswordFile),
            becomePasswordFile is null ? string.Empty : ToWslPath(becomePasswordFile),
            ToWslPath(playbookPath),
            "-i",
            ToWslPath(services.Paths.InventoryPath)
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
