namespace HomeOps.Cli.Configuration;

public sealed class HomeOpsConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string InfrastructureRepo { get; set; } = ".";
    public RepoConfig Repo { get; set; } = new();
    public ProxmoxConfig Proxmox { get; set; } = new();
    public TerraformConfig Terraform { get; set; } = new();
    public AnsibleConfig Ansible { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
    public AgentWorkflowConfig AgentWorkflow { get; set; } = new();
}

public sealed class RepoConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ProxmoxConfig
{
    public string Endpoint { get; set; } = string.Empty;
    public string Node { get; set; } = string.Empty;
    public string ImageStorage { get; set; } = string.Empty;
    public ProxmoxFeatureConfig Features { get; set; } = new();
}

public sealed class ProxmoxFeatureConfig
{
    public bool CloudImageDownloads { get; set; }
}

public sealed class TerraformConfig
{
    public string TargetsRoot { get; set; } = "terraform";
    public string PlanArtifactDir { get; set; } = ".homeops/plans";
    public Dictionary<string, TerraformTargetConfig> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TerraformTargetConfig
{
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
}

public sealed class AnsibleConfig
{
    public string PlaybooksRoot { get; set; } = "ansible";
    public string WslDistro { get; set; } = "Ubuntu";
    public string InventoryPath { get; set; } = "ansible/inventory";
    public string VaultPath { get; set; } = "inventory/production/group_vars/all/vault.yml";
    public Dictionary<string, AnsiblePlaybookConfig> Playbooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AnsiblePlaybookConfig
{
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultLimit { get; set; } = string.Empty;
}

public sealed class AuditConfig
{
    public string LogDir { get; set; } = ".homeops/audit";
}

public sealed class AgentWorkflowConfig
{
    public string[] InspectBeforeChange { get; set; } = [];
    public string[] ValidateBeforeApply { get; set; } = [];
}
