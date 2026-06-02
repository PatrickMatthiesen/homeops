using System.Text.Json;
using HomeOps.Cli.Configuration;

namespace HomeOps.Cli.Audit;

public sealed record AuditRecord(
    string Id,
    DateTimeOffset Timestamp,
    string User,
    string CommandCategory,
    string Subject,
    string RepoPath,
    string RepoCommit,
    bool DirtyWorktree,
    int ExitCode,
    string RiskLevel,
    string ConfirmationMode,
    string Summary);

public sealed class AuditWriter(PathResolver paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Write(AuditRecord record)
    {
        Directory.CreateDirectory(paths.AuditLogDir);
        var file = Path.Combine(paths.AuditLogDir, $"{record.Timestamp:yyyyMMddHHmmssfff}-{record.Id}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(record, JsonOptions));
        return record.Id;
    }
}
