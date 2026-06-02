namespace HomeOps.Cli.Configuration;

public sealed class HomeOpsConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string InfrastructureRepo { get; set; } = ".";
    public TerraformConfig Terraform { get; set; } = new();
    public AnsibleConfig Ansible { get; set; } = new();
    public AuditConfig Audit { get; set; } = new();
}

public sealed class TerraformConfig
{
    public string TargetsRoot { get; set; } = "terraform";
    public string PlanArtifactDir { get; set; } = ".homeops/plans";
}

public sealed class AnsibleConfig
{
    public string PlaybooksRoot { get; set; } = "ansible";
    public string WslDistro { get; set; } = "Ubuntu";
    public string InventoryPath { get; set; } = "ansible/inventory";
}

public sealed class AuditConfig
{
    public string LogDir { get; set; } = ".homeops/audit";
}
