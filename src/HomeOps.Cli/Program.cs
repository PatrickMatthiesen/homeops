using HomeOps.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("homeops");
    config.SetApplicationVersion("0.1.1");

    config.AddCommand<LoginCommand>("login");
    config.AddCommand<LogoutCommand>("logout");
    config.AddCommand<DoctorCommand>("doctor");

    config.AddBranch("proxmox", proxmox =>
    {
        proxmox.AddCommand<ProxmoxStatusCommand>("status");
        proxmox.AddCommand<ProxmoxNodesCommand>("nodes");
        proxmox.AddCommand<ProxmoxVmsCommand>("vms");
        proxmox.AddCommand<ProxmoxStorageCommand>("storage");
    });

    config.AddBranch("terraform", terraform =>
    {
        terraform.AddCommand<TerraformFmtCommand>("fmt");
        terraform.AddCommand<TerraformValidateCommand>("validate");
        terraform.AddCommand<TerraformPlanCommand>("plan");
        terraform.AddCommand<TerraformApplyCommand>("apply");
    });

    config.AddBranch("ansible", ansible =>
    {
        ansible.AddCommand<AnsibleSyntaxCommand>("syntax");
        ansible.AddCommand<AnsibleCheckCommand>("check");
        ansible.AddCommand<AnsibleApplyCommand>("apply");
    });
});

return app.Run(args);
