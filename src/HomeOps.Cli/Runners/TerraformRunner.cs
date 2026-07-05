using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Risk;

namespace HomeOps.Cli.Runners;

public sealed class TerraformRunner(AppServices services)
{
    public async Task<CommandResult> FmtAsync(bool check)
    {
        var args = new List<string> { $"-chdir={services.Paths.TerraformRoot}", "fmt", "-recursive" };
        if (check)
        {
            args.Add("-check");
        }

        return await RunAsync(args, "terraform.fmt", services.Paths.TerraformRoot, "normal", "none");
    }

    public async Task<CommandResult> ValidateAsync(string target)
    {
        var targetPath = services.Paths.ResolveTerraformTarget(target);
        return await RunAsync([$"-chdir={targetPath}", "validate"], "terraform.validate", target, "normal", "none");
    }

    public async Task<CommandResult> PlanAsync(string target, bool json, bool writePlan)
    {
        var targetPath = services.Paths.ResolveTerraformTarget(target);
        var args = new List<string> { $"-chdir={targetPath}", "plan", "-input=false" };
        var planId = default(string);
        if (json)
        {
            args.Add("-json");
        }

        if (writePlan)
        {
            Directory.CreateDirectory(services.Paths.PlanArtifactDir);
            planId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{SafeName(target)}";
            args.Add($"-out={Path.Combine(services.Paths.PlanArtifactDir, planId + ".tfplan")}");
        }

        var result = await RunAsync(
            args,
            "terraform.plan",
            target,
            "normal",
            "none",
            includeSshPublicKey: true,
            summarize: RunnerHelpers.SummarizeTerraformPlan);
        if (planId is not null)
        {
            result = result with
            {
                Subject = $"{target} planId={planId}",
                Summary = AppendSummaryLine(result.Summary, $"Saved plan: {planId}")
            };
        }
        else if (result.ExitCode == 0 && !json)
        {
            result = result with
            {
                Summary = AppendSummaryLine(result.Summary, "Plan not saved. Re-run with --out before apply to guarantee the exact actions.")
            };
        }

        return result;
    }

    public async Task<CommandResult> ApplyAsync(string target, string? planId, bool yes)
    {
        var targetPath = services.Paths.ResolveTerraformTarget(target);
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);
        var redactor = RunnerHelpers.BuildRedactor(services.Credentials);
        var args = new List<string> { $"-chdir={targetPath}", "apply", "-input=false" };
        var hasPlanArtifact = !string.IsNullOrWhiteSpace(planId);
        if (!yes && !hasPlanArtifact)
        {
            return new CommandResult(
                2,
                "terraform.apply",
                target,
                "high",
                ConfirmationRequired: true,
                "terraform apply without a saved plan requires --yes to avoid an interactive prompt.",
                string.Empty,
                string.Empty,
                null);
        }

        if (hasPlanArtifact)
        {
            args.Add(ResolvePlanArtifact(planId!));
        }
        else if (yes)
        {
            args.Add("-auto-approve");
        }

        var process = await services.Processes.RunAsync(new("terraform", args, services.Paths.RepoRoot, TerraformEnvironment(includeSshPublicKey: true)));
        var redactedStdout = redactor.Redact(process.Stdout);
        var risk = RiskDetector.TerraformApply(redactedStdout, git.IsDirty, hasPlanArtifact);
        var result = RunnerHelpers.ToCommandResult(process, redactor, "terraform.apply", target, risk, null, !yes);
        var auditId = services.Audit.Write(RunnerHelpers.CreateAudit("terraform.apply", target, services.Paths.RepoRoot, git, result.ExitCode, risk, yes ? "--yes" : "none", result.Summary));
        return result with { AuditEventId = auditId };
    }

    private async Task<CommandResult> RunAsync(
        IReadOnlyList<string> args,
        string category,
        string subject,
        string risk,
        string confirmationMode,
        bool includeSshPublicKey = false,
        Func<string, string, int, string>? summarize = null)
    {
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);
        var redactor = RunnerHelpers.BuildRedactor(services.Credentials);
        var process = await services.Processes.RunAsync(new("terraform", args, services.Paths.RepoRoot, TerraformEnvironment(includeSshPublicKey)));
        var result = RunnerHelpers.ToCommandResult(process, redactor, category, subject, risk, null, summarize: summarize);
        var auditId = services.Audit.Write(RunnerHelpers.CreateAudit(category, subject, services.Paths.RepoRoot, git, result.ExitCode, risk, confirmationMode, result.Summary));
        return result with { AuditEventId = auditId };
    }

    private Dictionary<string, string?> TerraformEnvironment(bool includeSshPublicKey)
    {
        var endpoint = services.Credentials.Get(CredentialKeys.ProxmoxEndpoint);
        var token = services.Credentials.Get(CredentialKeys.ProxmoxTerraformToken);
        var environment = new Dictionary<string, string?>
        {
            ["PROXMOX_VE_ENDPOINT"] = endpoint,
            ["PROXMOX_VE_API_TOKEN"] = token,
            ["PM_API_URL"] = endpoint,
            ["PM_API_TOKEN_SECRET"] = token
        };

        if (includeSshPublicKey)
        {
            environment["TF_VAR_ssh_public_key"] = ReadSshPublicKey();
        }

        return environment;
    }

    private string ReadSshPublicKey()
    {
        var configuredPath = services.Credentials.Get(CredentialKeys.SshDeployKeyPath);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("SSH deploy key path is not configured. Run homeops login.");
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath);
        var publicKeyPath = expandedPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase)
            ? expandedPath
            : expandedPath + ".pub";
        if (!File.Exists(publicKeyPath))
        {
            throw new InvalidOperationException("SSH deploy public key file was not found next to the configured deploy key.");
        }

        var publicKey = File.ReadAllText(publicKeyPath).Trim();
        var fields = publicKey.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !IsPublicKeyType(fields[0]))
        {
            throw new InvalidOperationException("SSH deploy public key file does not contain an OpenSSH public key.");
        }

        try
        {
            _ = Convert.FromBase64String(fields[1]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("SSH deploy public key file does not contain valid OpenSSH public key data.");
        }

        return publicKey;
    }

    private static bool IsPublicKeyType(string value) =>
        value.StartsWith("ssh-", StringComparison.Ordinal) ||
        value.StartsWith("ecdsa-", StringComparison.Ordinal) ||
        value.StartsWith("sk-", StringComparison.Ordinal);

    private string ResolvePlanArtifact(string planId)
    {
        if (planId.Any(ch => (ch is '/' or '\\' or ':') || char.IsWhiteSpace(ch)))
        {
            throw new InvalidOperationException("plan-id must be a simple artifact id.");
        }

        var path = Path.Combine(services.Paths.PlanArtifactDir, planId.EndsWith(".tfplan", StringComparison.Ordinal) ? planId : planId + ".tfplan");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Plan artifact not found: {planId}");
        }

        return path;
    }

    private static string SafeName(string value)
    {
        return string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
    }

    private static string AppendSummaryLine(string summary, string line) =>
        string.IsNullOrWhiteSpace(summary)
            ? line
            : string.Join(Environment.NewLine, summary, line);
}
