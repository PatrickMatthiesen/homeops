namespace HomeOps.Cli.Risk;

public static class RiskDetector
{
    public static string TerraformApply(string stdout, bool dirtyWorktree, bool hasPlanArtifact)
    {
        var text = stdout.ToLowerInvariant();
        if (dirtyWorktree || !hasPlanArtifact || text.Contains("destroy") || text.Contains("replace") || ExtractChangeCount(text) >= 10)
        {
            return "high";
        }

        return "normal";
    }

    public static string AnsibleApply(bool dirtyWorktree, string? limit, bool unexpectedPath)
    {
        if (dirtyWorktree || unexpectedPath || string.IsNullOrWhiteSpace(limit) || limit.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return "high";
        }

        return "normal";
    }

    private static int ExtractChangeCount(string text)
    {
        return ExtractBefore(text, " to add") + ExtractBefore(text, " to change") + ExtractBefore(text, " to destroy");
    }

    private static int ExtractBefore(string text, string suffix)
    {
        var index = text.IndexOf(suffix, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0;
        }

        var start = index - 1;
        while (start >= 0 && char.IsDigit(text[start]))
        {
            start--;
        }

        return int.TryParse(text[(start + 1)..index], out var value) ? value : 0;
    }
}
