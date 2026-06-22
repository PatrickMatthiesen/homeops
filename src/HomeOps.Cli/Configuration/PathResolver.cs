namespace HomeOps.Cli.Configuration;

public sealed class PathResolver(HomeOpsConfig config)
{
    public string RepoRoot => Path.GetFullPath(config.InfrastructureRepo);
    public string TerraformRoot => Path.GetFullPath(Path.Combine(RepoRoot, config.Terraform.TargetsRoot));
    public string AnsibleRoot => Path.GetFullPath(Path.Combine(RepoRoot, config.Ansible.PlaybooksRoot));
    public string PlanArtifactDir => Path.GetFullPath(Path.Combine(RepoRoot, config.Terraform.PlanArtifactDir));
    public string AuditLogDir => Path.GetFullPath(Path.Combine(RepoRoot, config.Audit.LogDir));
    public string InventoryPath => Path.GetFullPath(Path.Combine(RepoRoot, config.Ansible.InventoryPath));
    public string VaultPath => ResolveUnderRoot(RepoRoot, config.Ansible.VaultPath, "Ansible vault");

    public string ResolveTerraformTarget(string target)
    {
        var requestedPath = config.Terraform.Targets.TryGetValue(target, out var configuredTarget) &&
            !string.IsNullOrWhiteSpace(configuredTarget.Path)
            ? configuredTarget.Path
            : Path.Combine(config.Terraform.TargetsRoot, target);

        return ResolveUnderRoot(RepoRoot, requestedPath, "Terraform target");
    }

    public string ResolveAnsiblePlaybook(string playbook)
    {
        var requestedPath = ResolveAnsiblePlaybookPath(playbook);
        return ResolveUnderRoot(RepoRoot, requestedPath, "Ansible playbook");
    }

    public string ResolveAnsiblePlaybookPath(string playbook)
    {
        return config.Ansible.Playbooks.TryGetValue(playbook, out var configuredPlaybook) &&
            !string.IsNullOrWhiteSpace(configuredPlaybook.Path)
            ? configuredPlaybook.Path
            : Path.Combine(config.Ansible.PlaybooksRoot, playbook);
    }

    public string? ResolveAnsibleDefaultLimit(string playbook)
    {
        return config.Ansible.Playbooks.TryGetValue(playbook, out var configuredPlaybook) &&
            !string.IsNullOrWhiteSpace(configuredPlaybook.DefaultLimit)
            ? configuredPlaybook.DefaultLimit
            : null;
    }

    public static string ResolveUnderRoot(string root, string requestedPath, string description)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw new InvalidOperationException($"{description} is required.");
        }

        var normalizedRoot = Path.GetFullPath(root);
        var candidate = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(normalizedRoot, requestedPath));

        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{description} must be under configured root {normalizedRoot}.");
        }

        return candidate;
    }
}
