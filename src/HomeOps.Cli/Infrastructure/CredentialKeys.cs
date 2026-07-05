namespace HomeOps.Cli.Infrastructure;

public static class CredentialKeys
{
    public const string ProxmoxEndpoint = "proxmox.endpoint";
    public const string ProxmoxInspectToken = "proxmox.inspect.token";
    public const string ProxmoxTerraformToken = "proxmox.terraform.token";
    public const string AnsibleVaultPassword = "ansible.vault_password";
    public const string AnsibleBecomePassword = "ansible.become_password";
    public const string SshDeployKeyPath = "ssh.deploy_key_path";
    public const string SshDeployKeyPassphrase = "ssh.deploy_key_passphrase";

    public static readonly string[] Required =
    [
        ProxmoxEndpoint,
        ProxmoxInspectToken,
        ProxmoxTerraformToken,
        AnsibleVaultPassword,
        SshDeployKeyPath
    ];

    public static readonly string[] Optional =
    [
        AnsibleBecomePassword,
        SshDeployKeyPassphrase
    ];

    public static string ToTargetName(string key) => $"homeops:{key}";
}
