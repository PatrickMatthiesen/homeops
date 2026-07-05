using System.Text.Json;
using Spectre.Console;

namespace HomeOps.Cli.Output;

public static class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write(object value, bool text)
    {
        if (!text)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToJsonValue(value), JsonOptions));
            return;
        }

        if (value is CommandResult result)
        {
            AnsiConsole.MarkupLine($"[bold]{result.CommandCategory}[/] [grey]{result.Subject}[/]");
            AnsiConsole.MarkupLine($"exitCode: {result.ExitCode} risk: {result.RiskLevel} audit: {result.AuditEventId ?? "-"}");
            if (!string.IsNullOrWhiteSpace(result.Summary))
            {
                AnsiConsole.WriteLine(result.Summary);
            }

            return;
        }

        AnsiConsole.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static object ToJsonValue(object value)
    {
        if (value is not CommandResult result || result.ExitCode != 0)
        {
            return value;
        }

        return new
        {
            result.ExitCode,
            result.CommandCategory,
            result.Subject,
            result.RiskLevel,
            result.ConfirmationRequired,
            result.Summary,
            result.AuditEventId
        };
    }
}

public sealed record CommandResult(
    int ExitCode,
    string CommandCategory,
    string Subject,
    string RiskLevel,
    bool ConfirmationRequired,
    string Summary,
    string Stdout,
    string Stderr,
    string? AuditEventId);
