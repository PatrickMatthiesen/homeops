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

        var result = await RunAsync(args, "terraform.plan", target, "normal", "none");
        if (planId is not null)
        {
            result = result with { Subject = $"{target} planId={planId}" };
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

        var process = await services.Processes.RunAsync(new("terraform", args, services.Paths.RepoRoot, TerraformEnvironment()));
        var redactedStdout = redactor.Redact(process.Stdout);
        var risk = RiskDetector.TerraformApply(redactedStdout, git.IsDirty, hasPlanArtifact);
        var result = RunnerHelpers.ToCommandResult(process, redactor, "terraform.apply", target, risk, null, !yes);
        var auditId = services.Audit.Write(RunnerHelpers.CreateAudit("terraform.apply", target, services.Paths.RepoRoot, git, result.ExitCode, risk, yes ? "--yes" : "none", result.Summary));
        return result with { AuditEventId = auditId };
    }

    private async Task<CommandResult> RunAsync(IReadOnlyList<string> args, string category, string subject, string risk, string confirmationMode)
    {
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);
        var redactor = RunnerHelpers.BuildRedactor(services.Credentials);
        var process = await services.Processes.RunAsync(new("terraform", args, services.Paths.RepoRoot, TerraformEnvironment()));
        var result = RunnerHelpers.ToCommandResult(process, redactor, category, subject, risk, null);
        var auditId = services.Audit.Write(RunnerHelpers.CreateAudit(category, subject, services.Paths.RepoRoot, git, result.ExitCode, risk, confirmationMode, result.Summary));
        return result with { AuditEventId = auditId };
    }

    private Dictionary<string, string?> TerraformEnvironment()
    {
        var endpoint = services.Credentials.Get(CredentialKeys.ProxmoxEndpoint);
        var token = services.Credentials.Get(CredentialKeys.ProxmoxTerraformToken);
        return new Dictionary<string, string?>
        {
            ["PROXMOX_VE_ENDPOINT"] = endpoint,
            ["PROXMOX_VE_API_TOKEN"] = token,
            ["PM_API_URL"] = endpoint,
            ["PM_API_TOKEN_SECRET"] = token
        };
    }

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
}
