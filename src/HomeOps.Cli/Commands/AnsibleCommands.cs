using System.ComponentModel;
using HomeOps.Cli.Output;
using HomeOps.Cli.Runners;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public sealed class AnsibleApplySettings : PlaybookSettings
{
    [CommandOption("--yes")]
    [Description("Allow apply to proceed in agent workflows and record risk in audit output.")]
    public bool Yes { get; init; }
}

public sealed class AnsibleSyntaxCommand : AsyncCommand<PlaybookSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PlaybookSettings settings)
    {
        var result = await new AnsibleRunner(AppServices.Create()).SyntaxAsync(settings.Playbook);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}

public sealed class AnsibleCheckCommand : AsyncCommand<PlaybookSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, PlaybookSettings settings)
    {
        var result = await new AnsibleRunner(AppServices.Create()).CheckAsync(settings.Playbook, settings.Limit);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}

public sealed class AnsibleApplyCommand : AsyncCommand<AnsibleApplySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, AnsibleApplySettings settings)
    {
        var result = await new AnsibleRunner(AppServices.Create()).ApplyAsync(settings.Playbook, settings.Limit, settings.Yes);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}
