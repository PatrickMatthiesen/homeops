using System.ComponentModel;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public class CommonSettings : CommandSettings
{
    [CommandOption("--text")]
    [Description("Print compact human-readable output instead of JSON.")]
    public bool Text { get; init; }
}

public class TargetSettings : CommonSettings
{
    [CommandArgument(0, "<target>")]
    public string Target { get; init; } = string.Empty;
}

public class PlaybookSettings : CommonSettings
{
    [CommandArgument(0, "<playbook>")]
    public string Playbook { get; init; } = string.Empty;

    [CommandOption("--limit")]
    public string? Limit { get; init; }
}

public class PrivilegedPlaybookSettings : PlaybookSettings
{
    [CommandOption("--become")]
    [Description("Run Ansible with privilege escalation using the brokered ansible.become_password credential.")]
    public bool Become { get; init; }
}
