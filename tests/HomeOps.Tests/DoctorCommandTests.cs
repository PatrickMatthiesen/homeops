using HomeOps.Cli.Commands;
using HomeOps.Cli.Execution;

namespace HomeOps.Tests;

public sealed class DoctorCommandTests
{
    [Fact]
    public void AnsibleProbeTargetsConfiguredWslDistro()
    {
        var request = DoctorCommand.BuildAnsibleProbe("Ubuntu", @"D:\repo");

        Assert.Equal("wsl.exe", request.FileName);
        Assert.Equal(
            ["-d", "Ubuntu", "--", "bash", "-lc", "export ANSIBLE_LOCAL_TEMP=/tmp/homeops-ansible; command -v ansible-playbook >/dev/null 2>&1 || exit 127; ansible-playbook --version"],
            request.Arguments);
    }

    [Fact]
    public void MissingAnsibleReportsExactSupportedSetupCommand()
    {
        var status = DoctorCommand.BuildAnsibleStatus(
            new ProcessResult(127, string.Empty, "sh: ansible-playbook: not found"),
            "Ubuntu");

        Assert.False(status.Available);
        Assert.Contains("sudo apt-get install -y ansible", status.SetupCommand);
        Assert.Contains("Ubuntu", status.SetupCommand);
    }

    [Fact]
    public void InstalledAnsibleReportsVersion()
    {
        var status = DoctorCommand.BuildAnsibleStatus(
            new ProcessResult(0, "ansible-playbook [core 2.16.3]\nconfig file = None", string.Empty),
            "Ubuntu");

        Assert.True(status.Available);
        Assert.Equal("ansible-playbook [core 2.16.3]", status.Version);
        Assert.Null(status.SetupCommand);
    }
}
