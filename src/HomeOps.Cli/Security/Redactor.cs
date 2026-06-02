using System.Text.RegularExpressions;

namespace HomeOps.Cli.Security;

public sealed class Redactor(IEnumerable<string?> secrets)
{
    private static readonly Regex TokenPattern = new(@"(?i)\b([a-z0-9_.-]*(token|secret|password|passwd|apikey|api_key)[a-z0-9_.-]*)(\s*[:=]\s*)([^\s""']+)", RegexOptions.Compiled);
    private static readonly Regex PrivateKeyPattern = new(@"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly string[] _secrets = secrets
        .Where(secret => !string.IsNullOrWhiteSpace(secret) && secret.Length >= 3)
        .Distinct(StringComparer.Ordinal)
        .ToArray()!;

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = value;
        foreach (var secret in _secrets)
        {
            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
        }

        redacted = PrivateKeyPattern.Replace(redacted, "[REDACTED]");
        redacted = TokenPattern.Replace(redacted, "$1$3[REDACTED]");
        return redacted;
    }
}
