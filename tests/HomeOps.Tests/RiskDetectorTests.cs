using HomeOps.Cli.Risk;

namespace HomeOps.Tests;

public sealed class RiskDetectorTests
{
    [Fact]
    public void TerraformApplyIsHighRiskForDirtyWorktree()
    {
        Assert.Equal("high", RiskDetector.TerraformApply("No changes.", dirtyWorktree: true, hasPlanArtifact: true));
    }

    [Fact]
    public void TerraformApplyIsHighRiskWithoutPlanArtifact()
    {
        Assert.Equal("high", RiskDetector.TerraformApply("No changes.", dirtyWorktree: false, hasPlanArtifact: false));
    }

    [Fact]
    public void AnsibleApplyIsHighRiskWithoutLimit()
    {
        Assert.Equal("high", RiskDetector.AnsibleApply(dirtyWorktree: false, limit: null, unexpectedPath: false));
    }
}
