using HomeOps.Cli.Audit;
using HomeOps.Cli.Configuration;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;

namespace HomeOps.Cli;

public sealed record AppServices(
    HomeOpsConfig Config,
    PathResolver Paths,
    ICredentialStore Credentials,
    IProcessRunner Processes,
    GitInfo Git,
    AuditWriter Audit)
{
    public static AppServices Create()
    {
        var config = ConfigLoader.Load();
        var paths = new PathResolver(config);
        var processes = new ProcessRunner();
        return new AppServices(
            config,
            paths,
            new WindowsCredentialStore(),
            processes,
            new GitInfo(processes),
            new AuditWriter(paths));
    }
}
