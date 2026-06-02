using Microsoft.Extensions.Configuration;

namespace HomeOps.Cli.Configuration;

public static class ConfigLoader
{
    public static HomeOpsConfig Load(string? path = null)
    {
        path ??= Path.Combine(Directory.GetCurrentDirectory(), "homeops.json");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Config file not found: {fullPath}");
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var config = configuration.Get<HomeOpsConfig>() ?? new HomeOpsConfig();
        if (config.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported schemaVersion {config.SchemaVersion}.");
        }

        return config;
    }
}
