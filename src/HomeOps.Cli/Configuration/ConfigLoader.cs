using Microsoft.Extensions.Configuration;
using YamlDotNet.RepresentationModel;

namespace HomeOps.Cli.Configuration;

public static class ConfigLoader
{
    private static readonly string[] DefaultConfigNames = ["homeops.json", "homeops.yaml", "homeops.yml"];

    public static HomeOpsConfig Load(string? path = null)
    {
        var fullPath = Path.GetFullPath(path ?? FindDefaultConfigPath());
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Config file not found: {fullPath}");
        }

        var builder = new ConfigurationBuilder();
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddJsonFile(fullPath, optional: false, reloadOnChange: false);
        }
        else if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddInMemoryCollection(ReadYamlConfiguration(fullPath));
        }
        else
        {
            throw new InvalidOperationException($"Unsupported config file extension: {extension}");
        }

        var configuration = builder.Build();
        var config = configuration.Get<HomeOpsConfig>() ?? new HomeOpsConfig();
        if (config.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported schemaVersion {config.SchemaVersion}.");
        }

        return config;
    }

    private static string FindDefaultConfigPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        foreach (var fileName in DefaultConfigNames)
        {
            var candidate = Path.Combine(currentDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(currentDirectory, DefaultConfigNames[0]);
    }

    private static IReadOnlyDictionary<string, string?> ReadYamlConfiguration(string path)
    {
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (yaml.Documents.Count > 0)
        {
            FlattenYamlNode(yaml.Documents[0].RootNode, [], values);
        }

        return values;
    }

    private static void FlattenYamlNode(YamlNode node, IReadOnlyList<string> path, IDictionary<string, string?> values)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
                    {
                        continue;
                    }

                    FlattenYamlNode(valueNode, [.. path, NormalizeKey(key.Value, path)], values);
                }

                break;

            case YamlSequenceNode sequence:
                for (var index = 0; index < sequence.Children.Count; index++)
                {
                    FlattenYamlNode(sequence.Children[index], [.. path, index.ToString()], values);
                }

                break;

            case YamlScalarNode scalar when path.Count > 0:
                values[string.Join(':', path)] = scalar.Value;
                break;
        }
    }

    private static string NormalizeKey(string key, IReadOnlyList<string> path)
    {
        if (IsNamedConfigEntry(path))
        {
            return key;
        }

        return key switch
        {
            "schema_version" or "schema-version" => "schemaVersion",
            "infrastructure_repo" or "infrastructure-repo" => "infrastructureRepo",
            "targets_root" or "targets-root" => "targetsRoot",
            "plan_artifact_dir" or "plan-artifact-dir" => "planArtifactDir",
            "playbooks_root" or "playbooks-root" => "playbooksRoot",
            "wsl_distro" or "wsl-distro" => "wslDistro",
            "inventory" or "inventory_path" or "inventory-path" => "inventoryPath",
            "default_limit" or "default-limit" => "defaultLimit",
            "agent_workflow" or "agent-workflow" => "agentWorkflow",
            "inspect_before_change" or "inspect-before-change" => "inspectBeforeChange",
            "validate_before_apply" or "validate-before-apply" => "validateBeforeApply",
            "log_dir" or "log-dir" => "logDir",
            _ => key
        };
    }

    private static bool IsNamedConfigEntry(IReadOnlyList<string> path)
    {
        if (path.Count < 2)
        {
            return false;
        }

        var section = path[^2];
        var collection = path[^1];
        return section.Equals("terraform", StringComparison.OrdinalIgnoreCase) &&
            collection.Equals("targets", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("ansible", StringComparison.OrdinalIgnoreCase) &&
            collection.Equals("playbooks", StringComparison.OrdinalIgnoreCase);
    }
}
