using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace HomeOps.Cli.Commands;

public sealed class LoginCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings)
    {
        var store = new WindowsCredentialStore();
        store.Set(CredentialKeys.ProxmoxEndpoint, Prompt("Proxmox endpoint"));
        store.Set(CredentialKeys.ProxmoxInspectToken, Secret("Proxmox inspect token"));
        store.Set(CredentialKeys.ProxmoxTerraformToken, Secret("Proxmox Terraform token"));
        store.Set(CredentialKeys.AnsibleVaultPassword, Secret("Ansible vault password"));
        store.Set(CredentialKeys.SshDeployKeyPath, Prompt("SSH deploy key path"));
        var passphrase = Secret("SSH deploy key passphrase (empty allowed)", allowEmpty: true);
        if (!string.IsNullOrEmpty(passphrase))
        {
            store.Set(CredentialKeys.SshDeployKeyPassphrase, passphrase);
        }

        OutputWriter.Write(new { status = "ok", message = "Credentials stored in Windows Credential Manager." }, settings.Text);
        return 0;
    }

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
}

public sealed class LogoutCommand : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings)
    {
        var store = new WindowsCredentialStore();
        foreach (var key in CredentialKeys.Required.Append(CredentialKeys.SshDeployKeyPassphrase))
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
        var metadata = services.Credentials.ListMetadata(CredentialKeys.Required.Append(CredentialKeys.SshDeployKeyPassphrase));
        OutputWriter.Write(new
        {
            status = "ok",
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
                ansible = new { available = false, note = "Ansible runs through the configured WSL distro; no distro is installed until wsl -l -v succeeds." }
            },
            credentials = metadata
        }, settings.Text);
        return 0;
    }

    private static string CleanToolOutput(string value) => value.Replace("\0", string.Empty).Trim();
}
