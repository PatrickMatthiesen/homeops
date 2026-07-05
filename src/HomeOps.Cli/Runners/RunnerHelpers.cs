using HomeOps.Cli.Audit;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Security;
using System.Text.RegularExpressions;

namespace HomeOps.Cli.Runners;

public static class RunnerHelpers
{
    public static Redactor BuildRedactor(ICredentialStore store)
    {
        return new Redactor(CredentialKeys.Required
            .Concat(CredentialKeys.Optional)
            .Select(store.Get));
    }

    public static CommandResult ToCommandResult(
        ProcessResult process,
        Redactor redactor,
        string category,
        string subject,
        string risk,
        string? auditEventId,
        bool confirmationRequired = false,
        Func<string, string, int, string>? summarize = null)
    {
        var stdout = redactor.Redact(process.Stdout);
        var stderr = redactor.Redact(process.Stderr);
        var summary = summarize?.Invoke(stdout, stderr, process.ExitCode) ?? Summarize(stdout, stderr, process.ExitCode);
        return new CommandResult(process.ExitCode, category, subject, risk, confirmationRequired, summary, stdout, stderr, auditEventId);
    }

    public static AuditRecord CreateAudit(
        string category,
        string subject,
        string repoPath,
        GitSnapshot git,
        int exitCode,
        string risk,
        string confirmationMode,
        string summary)
    {
        return new AuditRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            Environment.UserName,
            category,
            subject,
            repoPath,
            git.Commit,
            git.IsDirty,
            exitCode,
            risk,
            confirmationMode,
            summary);
    }

    public static string SummarizeTerraformPlan(string stdout, string stderr, int exitCode)
    {
        var fallback = Summarize(stdout, stderr, exitCode);
        var source = StripAnsi(!string.IsNullOrWhiteSpace(stdout) ? stdout : stderr);
        var lines = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return fallback;
        }

        var summary = lines.FirstOrDefault(line => line.StartsWith("Plan:", StringComparison.Ordinal));
        if (summary is null)
        {
            return fallback;
        }

        var details = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var resource = Regex.Match(lines[index], @"^# (?<name>\S+) will\s*be (?<action>.+)$");
            if (!resource.Success)
            {
                continue;
            }

            details.Add($"{resource.Groups["name"].Value}: {resource.Groups["action"].Value}");
            for (index++; index < lines.Length; index++)
            {
                var line = lines[index];
                if (Regex.IsMatch(line, @"^# \S+ will\s*be ") || line.StartsWith("Plan:", StringComparison.Ordinal))
                {
                    index--;
                    break;
                }

                var change = Regex.Match(line, @"^~\s+(?<name>[A-Za-z0-9_.-]+)\s+=\s+(?<from>.+?)\s+->\s+(?<to>.+)$");
                if (change.Success)
                {
                    details.Add($"{change.Groups["name"].Value}: {change.Groups["from"].Value} -> {change.Groups["to"].Value}");
                }
            }
        }

        return details.Count == 0
            ? summary
            : string.Join(Environment.NewLine, details.Prepend(summary));
    }

    private static string Summarize(string stdout, string stderr, int exitCode)
    {
        var source = !string.IsNullOrWhiteSpace(stdout) ? stdout : stderr;
        var lines = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return exitCode == 0 ? "Command completed successfully." : "Command failed without output.";
        }

        return string.Join(Environment.NewLine, lines.TakeLast(Math.Min(lines.Length, 8)));
    }

    private static string StripAnsi(string value) =>
        Regex.Replace(value, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
}
