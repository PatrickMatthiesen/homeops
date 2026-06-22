using System.ComponentModel;
using HomeOps.Cli.Output;
using HomeOps.Cli.Proxmox;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public abstract class ProxmoxCommand(string path) : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var services = AppServices.Create();
        var result = await GetAsync(new ProxmoxClient(services.Credentials));
        OutputWriter.Write(result, settings.Text);
        return 0;
    }

    protected virtual Task<object> GetAsync(ProxmoxClient client) => client.GetAsync(path);
}

public sealed class ProxmoxStatusCommand() : ProxmoxCommand("cluster/resources");
public sealed class ProxmoxNodesCommand() : ProxmoxCommand("nodes");
public sealed class ProxmoxVmsCommand() : ProxmoxCommand("cluster/resources")
{
    protected override Task<object> GetAsync(ProxmoxClient client) => client.GetVirtualMachinesAsync();
}
public sealed class ProxmoxStorageCommand() : ProxmoxCommand("storage");

public sealed class ProxmoxStorageContentSettings : CommonSettings
{
    [CommandOption("--content")]
    [Description("Only include one Proxmox content type, such as iso, vztmpl, images, rootdir, or backup.")]
    public string? Content { get; init; }
}

public sealed class ProxmoxStorageContentCommand : AsyncCommand<ProxmoxStorageContentSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ProxmoxStorageContentSettings settings)
    {
        var services = AppServices.Create();
        var result = await new ProxmoxClient(services.Credentials).GetStorageContentAsync(settings.Content);
        OutputWriter.Write(result, settings.Text);
        return 0;
    }
}
