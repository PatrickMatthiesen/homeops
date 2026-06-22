using HomeOps.Cli.Configuration;

namespace HomeOps.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void LoadsYamlConfigWithCatalogEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "homeops.yml");
        File.WriteAllText(configPath, """
repo:
  name: HomeAnsible
  description: Homelab infrastructure Terraform and Ansible repository.

proxmox:
  endpoint: "https://192.0.2.10:8006/"
  node: example-node

terraform:
  targets:
    sample-app:
      path: terraform/sample-app
      description: Sample application container.
      host: app.example.test

ansible:
  inventory: inventory/production/hosts.ini
  playbooks:
    initial_setup:
      path: playbooks/initial_setup.yml
      description: Baseline users, SSH, network, and system setup.
    sample-app:
      path: playbooks/sample-app.yml
      description: Configure the sample application host.
      default_limit: app.example.test

agent_workflow:
  inspect_before_change:
    - homeops proxmox status --json
    - homeops proxmox vms --json
  validate_before_apply:
    - homeops terraform validate <target>
    - homeops ansible syntax <playbook>
""");

        var config = ConfigLoader.Load(configPath);

        Assert.Equal("HomeAnsible", config.Repo.Name);
        Assert.Equal("https://192.0.2.10:8006/", config.Proxmox.Endpoint);
        Assert.Equal("terraform/sample-app", config.Terraform.Targets["sample-app"].Path);
        Assert.Equal("inventory/production/hosts.ini", config.Ansible.InventoryPath);
        Assert.Equal("playbooks/initial_setup.yml", config.Ansible.Playbooks["initial_setup"].Path);
        Assert.Equal("app.example.test", config.Ansible.Playbooks["sample-app"].DefaultLimit);
        Assert.Equal(2, config.AgentWorkflow.InspectBeforeChange.Length);
        Assert.Equal("homeops ansible syntax <playbook>", config.AgentWorkflow.ValidateBeforeApply[1]);
    }

    [Fact]
    public void DiscoversYamlConfigWhenNoPathIsProvided()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "homeops.yaml"), """
schema_version: 1
infrastructure_repo: infra
""");

        try
        {
            Directory.SetCurrentDirectory(root);

            var config = ConfigLoader.Load();

            Assert.Equal("infra", config.InfrastructureRepo);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }
}
