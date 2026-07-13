using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using HomeOps.Cli.Proxmox;
using Spectre.Console;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public sealed class LoginCommand : Command<LoginSettings>
{
    private static readonly LoginSetting[] LoginSettings =
    [
        new(CredentialKeys.ProxmoxEndpoint, "Proxmox endpoint", false, false),
        new(CredentialKeys.ProxmoxInspectToken, "Proxmox read-only API token (user@realm!tokenid=secret)", true, false),
        new(CredentialKeys.ProxmoxTerraformToken, "Proxmox Terraform API token (user@realm!tokenid=secret)", true, false),
        new(CredentialKeys.AnsibleVaultPassword, "Ansible vault password", true, false),
        new(CredentialKeys.AnsibleBecomePassword, "Ansible become password (empty allowed)", true, true),
        new(CredentialKeys.SshDeployKeyPath, "SSH deploy key path", false, false),
        new(CredentialKeys.SshDeployKeyPassphrase, "SSH deploy key passphrase (empty allowed)", true, true)
    ];

    public override int Execute(CommandContext context, LoginSettings settings)
    {
        var store = new WindowsCredentialStore();
        var metadata = store.ListMetadata(LoginSettings.Select(setting => setting.Key));
        var selectedSettings = ShouldPromptForSelection(settings.Interactive, metadata)
            ? SelectSettings(metadata)
            : LoginSettings;

        foreach (var setting in selectedSettings)
        {
            var value = setting.IsSecret
                ? Secret(setting.Label, setting.AllowEmpty)
                : Prompt(setting.Label);

            if (!string.IsNullOrEmpty(value))
            {
                store.Set(setting.Key, value);
            }
        }

        OutputWriter.Write(new { status = "ok", message = "Credentials stored in Windows Credential Manager." }, settings.Text);
        return 0;
    }

    public static bool ShouldPromptForSelection(bool interactive, IReadOnlyDictionary<string, bool> metadata) =>
        interactive && metadata.Values.Any(configured => configured);

    public static string ConfigurationStatus(bool configured) =>
        configured ? "[green](set)[/]" : "[yellow](not set)[/]";

    private static IReadOnlyList<LoginSetting> SelectSettings(IReadOnlyDictionary<string, bool> metadata) =>
        AnsiConsole.Prompt(
            new MultiSelectionPrompt<LoginSetting>()
                .Title("Which login settings do you want to update?")
                .PageSize(LoginSettings.Length)
                .MoreChoicesText("[grey](Move up and down to reveal more settings)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle a setting, then [green]<enter>[/] to accept)[/]")
                .UseConverter(setting => $"{Markup.Escape(setting.Label)} {ConfigurationStatus(metadata[setting.Key])}")
                .AddChoices(LoginSettings));

    private static string Prompt(string label) => AnsiConsole.Ask<string>($"{label}:");

    private static string Secret(string label, bool allowEmpty = false)
    {
        var prompt = new TextPrompt<string>($"{label}:").Secret();
        if (allowEmpty)
        {
            prompt.AllowEmpty();
        }

        return AnsiConsole.Prompt(prompt);
    }

    private sealed record LoginSetting(string Key, string Label, bool IsSecret, bool AllowEmpty);
}

public sealed class LogoutCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings)
    {
        var store = new WindowsCredentialStore();
        foreach (var key in CredentialKeys.Required.Concat(CredentialKeys.Optional))
        {
            store.Delete(key);
        }

        OutputWriter.Write(new { status = "ok", message = "homeops credentials deleted." }, settings.Text);
        return 0;
    }
}

public sealed class DoctorCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var services = AppServices.Create();
        var terraform = await services.Processes.RunAsync(new("terraform", ["version"], services.Paths.RepoRoot, new Dictionary<string, string?>()));
        var wsl = await services.Processes.RunAsync(new("wsl.exe", ["-l", "-v"], services.Paths.RepoRoot, new Dictionary<string, string?>()));
        var ansible = await services.Processes.RunAsync(BuildAnsibleProbe(services.Config.Ansible.WslDistro, services.Paths.RepoRoot));
        var ansibleStatus = BuildAnsibleStatus(ansible, services.Config.Ansible.WslDistro);
        var metadata = services.Credentials.ListMetadata(CredentialKeys.Required.Concat(CredentialKeys.Optional));
        var proxmoxClient = new ProxmoxClient(services.Credentials);
        var proxmoxInspection = await proxmoxClient.CheckInspectionAccessAsync();
        var proxmoxTerraform = await proxmoxClient.CheckTerraformAccessAsync(
            services.Config.Proxmox.Node,
            services.Config.Proxmox.ImageStorage,
            services.Config.Proxmox.Features.CloudImageDownloads);
        var proxmoxReady = proxmoxInspection.AllAllowed && proxmoxTerraform.AllAllowed;
        var ready = proxmoxReady && terraform.ExitCode == 0 && wsl.ExitCode == 0 && ansibleStatus.Available;
        OutputWriter.Write(new
        {
            status = ready ? "ok" : "error",
            config = new
            {
                repo = services.Paths.RepoRoot,
                terraformRoot = services.Paths.TerraformRoot,
                ansibleRoot = services.Paths.AnsibleRoot,
                auditLogDir = services.Paths.AuditLogDir
            },
            tools = new
            {
                terraform = new { available = terraform.ExitCode == 0, version = terraform.Stdout.Trim() },
                wsl = new { available = wsl.ExitCode == 0, output = CleanToolOutput(wsl.Stdout), error = CleanToolOutput(wsl.Stderr) },
                ansible = ansibleStatus
            },
            credentials = metadata,
            proxmox = new
            {
                inspection = proxmoxInspection,
                terraform = proxmoxTerraform
            }
        }, settings.Text);
        return ready ? 0 : 1;
    }

    public static ProcessRequest BuildAnsibleProbe(string distro, string workingDirectory) =>
        new(
            "wsl.exe",
            ["-d", distro, "--", "bash", "-lc", "export ANSIBLE_LOCAL_TEMP=/tmp/homeops-ansible; command -v ansible-playbook >/dev/null 2>&1 || exit 127; ansible-playbook --version"],
            workingDirectory,
            new Dictionary<string, string?>());

    public static AnsibleToolStatus BuildAnsibleStatus(ProcessResult result, string distro)
    {
        const string install = "sudo apt-get update && sudo apt-get install -y ansible";
        return result.ExitCode == 0
            ? new AnsibleToolStatus(true, CleanToolOutput(result.Stdout).Split('\n', StringSplitOptions.TrimEntries)[0], string.Empty, null)
            : new AnsibleToolStatus(
                false,
                string.Empty,
                CleanToolOutput(result.Stderr),
                result.ExitCode == 127 ? $"wsl.exe -d {distro} -- bash -lc \"{install}\"" : null);
    }

    private static string CleanToolOutput(string value) => value.Replace("\0", string.Empty).Trim();
}

public sealed record AnsibleToolStatus(bool Available, string Version, string Error, string? SetupCommand);
