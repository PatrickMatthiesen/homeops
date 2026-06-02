using HomeOps.Cli.Output;
using HomeOps.Cli.Proxmox;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public abstract class ProxmoxCommand(string path) : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var services = AppServices.Create();
        var result = await new ProxmoxClient(services.Credentials).GetAsync(path);
        OutputWriter.Write(result, settings.Text);
        return 0;
    }
}

public sealed class ProxmoxStatusCommand() : ProxmoxCommand("cluster/resources");
public sealed class ProxmoxNodesCommand() : ProxmoxCommand("nodes");
public sealed class ProxmoxVmsCommand() : ProxmoxCommand("cluster/resources?type=vm");
public sealed class ProxmoxStorageCommand() : ProxmoxCommand("storage");
