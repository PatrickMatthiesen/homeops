using HomeOps.Cli.Security;

namespace HomeOps.Tests;

public sealed class RedactorTests
{
    [Fact]
    public void RedactsExactSecrets()
    {
        var redactor = new Redactor(["super-secret"]);
        Assert.Equal("value=[REDACTED]", redactor.Redact("value=super-secret"));
    }

    [Fact]
    public void RedactsTokenLikeAssignments()
    {
        var redactor = new Redactor([]);
        Assert.Equal("api_token=[REDACTED]", redactor.Redact("api_token=abc123"));
    }

    [Fact]
    public void RedactsPrivateKeyBlocks()
    {
        var input = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----";
        Assert.Equal("[REDACTED]", new Redactor([]).Redact(input));
    }
}
