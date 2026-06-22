using HomeOps.Cli;
using HomeOps.Cli.Audit;
using HomeOps.Cli.Configuration;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Runners;

namespace HomeOps.Tests;

public sealed class AnsibleVaultEditorTests
{
    [Fact]
    public void ReplacesOnlyEncryptedVaultAndPreservesAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "vault.yml");
        var source = Path.Combine(root, "edited.tmp");
        File.WriteAllText(destination, "$ANSIBLE_VAULT;1.1;AES256\noriginal");
        File.SetAttributes(destination, FileAttributes.ReadOnly);
        File.WriteAllText(source, "$ANSIBLE_VAULT;1.1;AES256\nedited");

        try
        {
            AnsibleVaultEditor.ReplaceEncryptedVault(source, destination);

            Assert.Contains("edited", File.ReadAllText(destination));
            Assert.True(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(destination, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsPlaintextEditAndPreservesOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "vault.yml");
        var source = Path.Combine(root, "edited.tmp");
        const string original = "$ANSIBLE_VAULT;1.1;AES256\noriginal";
        File.WriteAllText(destination, original);
        File.WriteAllText(source, "plaintext: forbidden");

        try
        {
            Assert.Throws<InvalidOperationException>(() => AnsibleVaultEditor.ReplaceEncryptedVault(source, destination));
            Assert.Equal(original, File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InteractiveRequestUsesConfiguredVaultAndNoPasswordValue()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var config = new HomeOpsConfig
        {
            InfrastructureRepo = root,
            Ansible = new AnsibleConfig { WslDistro = "Ubuntu", VaultPath = "inventory/vault.yml" }
        };
        var paths = new PathResolver(config);
        var processes = new FakeProcessRunner();
        var services = new AppServices(config, paths, new FakeCredentialStore(), processes, new GitInfo(processes), new AuditWriter(paths));
        var editor = new AnsibleVaultEditor(services);

        var request = editor.BuildRequest(paths.VaultPath, Path.Combine(root, "password.tmp"), Path.Combine(root, "edited.tmp"));

        Assert.Equal("wsl.exe", request.FileName);
        Assert.Contains("ansible-vault edit", request.Arguments[5]);
        Assert.Contains("shred -u", request.Arguments[5]);
        Assert.DoesNotContain("ansible.vault_password", string.Join(' ', request.Arguments));
        Assert.Contains("/mnt/", request.Arguments[^3]);
    }
}
