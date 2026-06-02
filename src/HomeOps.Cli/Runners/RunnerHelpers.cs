using HomeOps.Cli.Audit;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Security;

namespace HomeOps.Cli.Runners;

public static class RunnerHelpers
{
    public static Redactor BuildRedactor(ICredentialStore store)
    {
        return new Redactor(CredentialKeys.Required
            .Append(CredentialKeys.SshDeployKeyPassphrase)
            .Select(store.Get));
    }

    public static CommandResult ToCommandResult(
        ProcessResult process,
        Redactor redactor,
        string category,
        string subject,
        string risk,
        string? auditEventId,
        bool confirmationRequired = false)
    {
        var stdout = redactor.Redact(process.Stdout);
        var stderr = redactor.Redact(process.Stderr);
        var summary = Summarize(stdout, stderr, process.ExitCode);
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
}
