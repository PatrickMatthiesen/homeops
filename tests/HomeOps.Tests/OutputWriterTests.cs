using System.Text.Json;
using HomeOps.Cli.Output;

namespace HomeOps.Tests;

public sealed class OutputWriterTests
{
    [Fact]
    public void JsonOutputOmitsRawStreamsForSuccessfulCommandResults()
    {
        var output = WriteJson(new CommandResult(
            0,
            "terraform.plan",
            "web-panel",
            "normal",
            false,
            "Plan: 0 to add, 1 to change, 0 to destroy.",
            "large stdout",
            "large stderr",
            "audit-id"));

        using var document = JsonDocument.Parse(output);
        Assert.False(document.RootElement.TryGetProperty("stdout", out _));
        Assert.False(document.RootElement.TryGetProperty("stderr", out _));
        Assert.Equal("Plan: 0 to add, 1 to change, 0 to destroy.", document.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public void JsonOutputKeepsRawStreamsForFailedCommandResults()
    {
        var output = WriteJson(new CommandResult(
            1,
            "terraform.plan",
            "web-panel",
            "normal",
            false,
            "Command failed.",
            "diagnostic stdout",
            "diagnostic stderr",
            "audit-id"));

        using var document = JsonDocument.Parse(output);
        Assert.Equal("diagnostic stdout", document.RootElement.GetProperty("stdout").GetString());
        Assert.Equal("diagnostic stderr", document.RootElement.GetProperty("stderr").GetString());
    }

    private static string WriteJson(CommandResult result)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            OutputWriter.Write(result, text: false);
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
