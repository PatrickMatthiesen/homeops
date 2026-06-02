using System.ComponentModel;
using HomeOps.Cli.Output;
using HomeOps.Cli.Runners;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public sealed class TerraformFmtSettings : CommonSettings
{
    [CommandOption("--check")]
    public bool Check { get; init; }
}

public sealed class TerraformPlanSettings : TargetSettings
{
    [CommandOption("--json")]
    [Description("Pass Terraform's -json flag to plan.")]
    public bool Json { get; init; }

    [CommandOption("--out")]
    public bool Out { get; init; }
}

public sealed class TerraformApplySettings : TargetSettings
{
    [CommandOption("--plan-id")]
    public string? PlanId { get; init; }

    [CommandOption("--yes")]
    public bool Yes { get; init; }
}

public sealed class TerraformFmtCommand : AsyncCommand<TerraformFmtSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TerraformFmtSettings settings)
    {
        var result = await new TerraformRunner(AppServices.Create()).FmtAsync(settings.Check);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}

public sealed class TerraformValidateCommand : AsyncCommand<TargetSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TargetSettings settings)
    {
        var result = await new TerraformRunner(AppServices.Create()).ValidateAsync(settings.Target);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}

public sealed class TerraformPlanCommand : AsyncCommand<TerraformPlanSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TerraformPlanSettings settings)
    {
        var result = await new TerraformRunner(AppServices.Create()).PlanAsync(settings.Target, settings.Json, settings.Out);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}

public sealed class TerraformApplyCommand : AsyncCommand<TerraformApplySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, TerraformApplySettings settings)
    {
        var result = await new TerraformRunner(AppServices.Create()).ApplyAsync(settings.Target, settings.PlanId, settings.Yes);
        OutputWriter.Write(result, settings.Text);
        return result.ExitCode;
    }
}
