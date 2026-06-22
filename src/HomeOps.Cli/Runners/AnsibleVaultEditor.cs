using System.Text;
using HomeOps.Cli.Execution;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Output;

namespace HomeOps.Cli.Runners;

public sealed class AnsibleVaultEditor(AppServices services)
{
    private const string VaultHeader = "$ANSIBLE_VAULT;";

    public async Task<CommandResult> EditAsync(CancellationToken cancellationToken = default)
    {
        var vaultPath = services.Paths.VaultPath;
        if (!File.Exists(vaultPath))
        {
            throw new InvalidOperationException("Configured Ansible vault file does not exist.");
        }

        if (!IsEncryptedVault(vaultPath))
        {
            throw new InvalidOperationException("Configured Ansible vault file is not Ansible Vault encrypted.");
        }

        var password = services.Credentials.Get(CredentialKeys.AnsibleVaultPassword)
            ?? throw new InvalidOperationException("Missing ansible.vault_password.");
        var passwordFile = Path.Combine(Path.GetTempPath(), $"homeops-vault-password-{Guid.NewGuid():N}.tmp");
        var encryptedOutput = Path.Combine(Path.GetDirectoryName(vaultPath)!, $".homeops-vault-{Guid.NewGuid():N}.tmp");
        var git = await services.Git.SnapshotAsync(services.Paths.RepoRoot);

        try
        {
            File.WriteAllText(passwordFile, password, new UTF8Encoding(false));
            var exitCode = await services.Processes.RunInteractiveAsync(BuildRequest(vaultPath, passwordFile, encryptedOutput), cancellationToken);
            if (exitCode != 0)
            {
                return WriteResult(exitCode, vaultPath, git, "Vault editor exited without replacing the encrypted vault.");
            }

            ReplaceEncryptedVault(encryptedOutput, vaultPath);
            return WriteResult(0, vaultPath, git, "Encrypted vault updated successfully.");
        }
        finally
        {
            SecureDelete(passwordFile);
            SecureDelete(encryptedOutput);
        }
    }

    public InteractiveProcessRequest BuildRequest(string vaultPath, string passwordFile, string encryptedOutput)
    {
        const string script = "set -e; umask 077; work=\\$(mktemp -d /tmp/homeops-vault-edit.XXXXXX); cleanup() { find \"\\$work\" -type f -exec shred -u {} + 2>/dev/null || rm -rf \"\\$work\"; rmdir \"\\$work\" 2>/dev/null || true; }; trap cleanup EXIT HUP INT TERM; cp \"\\$1\" \"\\$work/vault.yml\"; cp \"\\$2\" \"\\$work/password\"; chmod 600 \"\\$work/vault.yml\" \"\\$work/password\"; ansible-vault edit --vault-password-file \"\\$work/password\" \"\\$work/vault.yml\"; grep -q '^\\$ANSIBLE_VAULT;' \"\\$work/vault.yml\"; cp \"\\$work/vault.yml\" \"\\$3\"";
        return new InteractiveProcessRequest(
            "wsl.exe",
            ["-d", services.Config.Ansible.WslDistro, "--", "bash", "-lc", script, "homeops", ToWslPath(vaultPath), ToWslPath(passwordFile), ToWslPath(encryptedOutput)],
            services.Paths.RepoRoot,
            new Dictionary<string, string?>());
    }

    public static bool IsEncryptedVault(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[VaultHeader.Length];
        return stream.Read(header) == header.Length && Encoding.ASCII.GetString(header) == VaultHeader;
    }

    public static void ReplaceEncryptedVault(string source, string destination)
    {
        if (!File.Exists(source) || !IsEncryptedVault(source))
        {
            throw new InvalidOperationException("Edited vault did not remain Ansible Vault encrypted; original was preserved.");
        }

        var attributes = File.GetAttributes(destination);
        File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
        try
        {
            File.Replace(source, destination, null, ignoreMetadataErrors: false);
        }
        finally
        {
            if (File.Exists(destination))
            {
                File.SetAttributes(destination, attributes);
            }
        }
    }

    private CommandResult WriteResult(int exitCode, string vaultPath, GitSnapshot git, string summary)
    {
        const string category = "ansible.vault.edit";
        const string risk = "high";
        var result = new CommandResult(exitCode, category, vaultPath, risk, false, summary, string.Empty, string.Empty, null);
        var auditId = services.Audit.Write(RunnerHelpers.CreateAudit(category, vaultPath, services.Paths.RepoRoot, git, exitCode, risk, "interactive", summary));
        return result with { AuditEventId = auditId };
    }

    private static void SecureDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            var zeros = new byte[8192];
            long remaining = stream.Length;
            stream.Position = 0;
            while (remaining > 0)
            {
                var count = (int)Math.Min(zeros.Length, remaining);
                stream.Write(zeros, 0, count);
                remaining -= count;
            }
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ToWslPath(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath);
        return $"/mnt/{char.ToLowerInvariant(full[0])}{full[2..].Replace('\\', '/')}";
    }
}
