using HomeOps.Cli.Commands;
using HomeOps.Cli.Infrastructure;

namespace HomeOps.Tests;

public sealed class LoginCommandTests
{
    [Fact]
    public void InteractiveLoginPromptsForSelectionWhenAnySettingExists()
    {
        var metadata = CredentialKeys.Required
            .Concat(CredentialKeys.Optional)
            .ToDictionary(key => key, key => key == CredentialKeys.ProxmoxEndpoint);

        Assert.True(LoginCommand.ShouldPromptForSelection(interactive: true, metadata));
    }

    [Fact]
    public void InteractiveLoginUsesFullLoginWhenNoSettingsExist()
    {
        var metadata = CredentialKeys.Required
            .Concat(CredentialKeys.Optional)
            .ToDictionary(key => key, _ => false);

        Assert.False(LoginCommand.ShouldPromptForSelection(interactive: true, metadata));
    }

    [Fact]
    public void NormalLoginAlwaysUsesFullLogin()
    {
        var metadata = CredentialKeys.Required
            .Concat(CredentialKeys.Optional)
            .ToDictionary(key => key, _ => true);

        Assert.False(LoginCommand.ShouldPromptForSelection(interactive: false, metadata));
    }

    [Theory]
    [InlineData(true, "[green](set)[/]")]
    [InlineData(false, "[yellow](not set)[/]")]
    public void ConfigurationStatusShowsWhetherSettingExists(bool configured, string expected)
    {
        Assert.Equal(expected, LoginCommand.ConfigurationStatus(configured));
    }
}
