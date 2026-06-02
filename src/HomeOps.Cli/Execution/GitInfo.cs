namespace HomeOps.Cli.Execution;

public sealed record GitSnapshot(string Commit, bool IsDirty);

public sealed class GitInfo(IProcessRunner runner)
{
    public async Task<GitSnapshot> SnapshotAsync(string repoRoot)
    {
        var commit = await runner.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], repoRoot, new Dictionary<string, string?>()));
        var status = await runner.RunAsync(new ProcessRequest("git", ["status", "--porcelain"], repoRoot, new Dictionary<string, string?>()));
        return new GitSnapshot(
            commit.ExitCode == 0 ? commit.Stdout.Trim() : "unknown",
            status.ExitCode != 0 || !string.IsNullOrWhiteSpace(status.Stdout));
    }
}
