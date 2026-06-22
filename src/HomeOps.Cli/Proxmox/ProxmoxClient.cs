using System.Text.Json;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Security;

namespace HomeOps.Cli.Proxmox;

public sealed class ProxmoxClient(ICredentialStore credentials, HttpMessageHandler? handler = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<object> GetAsync(string relativePath, CancellationToken cancellationToken = default)
        => await GetAsync(relativePath, CredentialKeys.ProxmoxInspectToken, cancellationToken);

    private async Task<object> GetAsync(string relativePath, string tokenCredentialKey, CancellationToken cancellationToken)
    {
        var endpoint = credentials.Get(CredentialKeys.ProxmoxEndpoint) ?? throw new InvalidOperationException("Missing proxmox.endpoint.");
        var token = credentials.Get(tokenCredentialKey) ?? throw new InvalidOperationException($"Missing {tokenCredentialKey}.");
        using var client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"PVEAPIToken={token}");
        using var response = await client.GetAsync($"api2/json/{relativePath.TrimStart('/')}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var redacted = new Redactor([endpoint, token]).Redact(body);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Proxmox request failed: {(int)response.StatusCode} {response.ReasonPhrase} {redacted}");
        }

        return JsonSerializer.Deserialize<object>(redacted, JsonOptions) ?? new { };
    }

    public async Task<object> GetVirtualMachinesAsync(CancellationToken cancellationToken = default)
    {
        var virtualMachines = new List<SortedDictionary<string, object?>>();
        var known = new HashSet<(string Type, int VmId)>();

        foreach (var resource in GetData(await GetAsync("cluster/resources", cancellationToken)))
        {
            if (!TryGetString(resource, "type", out var type) ||
                type is not ("qemu" or "lxc") ||
                !TryGetInt32(resource, "vmid", out var vmId))
            {
                continue;
            }

            virtualMachines.Add(ToDictionary(resource));
            known.Add((type, vmId));
        }

        foreach (var node in GetData(await GetAsync("nodes", cancellationToken)))
        {
            if (!TryGetString(node, "node", out var nodeName))
            {
                continue;
            }

            foreach (var type in new[] { "qemu", "lxc" })
            {
                foreach (var resource in GetData(await GetAsync($"nodes/{Uri.EscapeDataString(nodeName)}/{type}", cancellationToken)))
                {
                    if (!TryGetInt32(resource, "vmid", out var vmId) || !known.Add((type, vmId)))
                    {
                        continue;
                    }

                    var item = ToDictionary(resource);
                    item["type"] = type;
                    item["node"] = nodeName;
                    virtualMachines.Add(item);
                }
            }
        }

        return new { data = virtualMachines };
    }

    public async Task<object> GetStorageContentAsync(string? contentType = null, CancellationToken cancellationToken = default)
    {
        var content = new List<SortedDictionary<string, object?>>();
        foreach (var node in GetData(await GetAsync("nodes", cancellationToken)))
        {
            if (!TryGetString(node, "node", out var nodeName))
            {
                continue;
            }

            var escapedNode = Uri.EscapeDataString(nodeName);
            foreach (var storage in GetData(await GetAsync($"nodes/{escapedNode}/storage", cancellationToken)))
            {
                if (!TryGetString(storage, "storage", out var storageName))
                {
                    continue;
                }

                var escapedStorage = Uri.EscapeDataString(storageName);
                foreach (var item in GetData(await GetAsync($"nodes/{escapedNode}/storage/{escapedStorage}/content", cancellationToken)))
                {
                    if (!string.IsNullOrWhiteSpace(contentType) &&
                        (!TryGetString(item, "content", out var itemContent) ||
                         !itemContent.Equals(contentType, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var entry = ToDictionary(item);
                    entry["node"] = nodeName;
                    entry["storage"] = storageName;
                    content.Add(entry);
                }
            }
        }

        return new
        {
            data = content
                .OrderBy(item => GetSortValue(item, "node"), StringComparer.Ordinal)
                .ThenBy(item => GetSortValue(item, "storage"), StringComparer.Ordinal)
                .ThenBy(item => GetSortValue(item, "content"), StringComparer.Ordinal)
                .ThenBy(item => GetSortValue(item, "volid"), StringComparer.Ordinal)
                .ToArray()
        };
    }

    public async Task<ProxmoxAccessReport> CheckInspectionAccessAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<ProxmoxAccessCheck>();
        await CheckAsync("Cluster resources", "cluster/resources");
        var nodes = await CheckAsync("Nodes", "nodes");
        await CheckAsync("Storage", "storage");

        if (nodes is not null)
        {
            foreach (var node in GetData(nodes))
            {
                if (!TryGetString(node, "node", out var nodeName))
                {
                    continue;
                }

                var escapedNode = Uri.EscapeDataString(nodeName);
                await CheckAsync($"QEMU VMs on {nodeName}", $"nodes/{escapedNode}/qemu");
                await CheckAsync($"Containers on {nodeName}", $"nodes/{escapedNode}/lxc");
                var storages = await CheckAsync($"Storage on {nodeName}", $"nodes/{escapedNode}/storage");
                if (storages is null)
                {
                    continue;
                }

                foreach (var storage in GetData(storages))
                {
                    if (!TryGetString(storage, "storage", out var storageName))
                    {
                        continue;
                    }

                    var escapedStorage = Uri.EscapeDataString(storageName);
                    await CheckAsync(
                        $"Content in {storageName} on {nodeName}",
                        $"nodes/{escapedNode}/storage/{escapedStorage}/content");
                }
            }
        }

        return new ProxmoxAccessReport(
            checks.All(check => check.Allowed) ? "ok" : "error",
            ReadOnly: true,
            checks);

        async Task<object?> CheckAsync(string name, string path)
        {
            try
            {
                var response = await GetAsync(path, cancellationToken);
                checks.Add(new ProxmoxAccessCheck(name, path, Allowed: true, Error: null));
                return response;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
            {
                checks.Add(new ProxmoxAccessCheck(name, path, Allowed: false, exception.Message));
                return null;
            }
        }
    }

    public async Task<ProxmoxTerraformAccessReport> CheckTerraformAccessAsync(
        string node,
        string imageStorage,
        bool cloudImageDownloads,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync("access/permissions", CredentialKeys.ProxmoxTerraformToken, cancellationToken);
            var permissions = GetPermissionData(response);
            var checks = new List<ProxmoxTerraformAccessCheck>
            {
                CheckPrivileges("Audit infrastructure", permissions, "/", ["Sys.Audit"]),
                CheckPrivileges("Manage virtual machines", permissions, "/vms",
                [
                    "VM.Allocate", "VM.Audit", "VM.Clone", "VM.Config.CDROM", "VM.Config.Cloudinit",
                    "VM.Config.CPU", "VM.Config.Disk", "VM.Config.HWType", "VM.Config.Memory",
                    "VM.Config.Network", "VM.Config.Options", "VM.PowerMgmt"
                ]),
                CheckAnyPathPrivileges(
                    "Allocate storage",
                    permissions,
                    permissions.Keys.Where(path => path.StartsWith("/storage/", StringComparison.Ordinal)).DefaultIfEmpty("/storage"),
                    ["Datastore.AllocateSpace", "Datastore.Audit"])
            };

            if (cloudImageDownloads)
            {
                checks.Add(string.IsNullOrWhiteSpace(imageStorage)
                    ? MissingConfiguration("Store cloud images", "proxmox.imageStorage")
                    : CheckPrivileges(
                        "Store cloud images",
                        permissions,
                        $"/storage/{imageStorage}",
                        ["Datastore.AllocateSpace", "Datastore.AllocateTemplate", "Datastore.Audit"]));
                checks.Add(string.IsNullOrWhiteSpace(node)
                    ? MissingConfiguration("Download cloud images", "proxmox.node")
                    : CheckPrivileges(
                        "Download cloud images",
                        permissions,
                        $"/nodes/{node}",
                        ["Sys.AccessNetwork"]));
            }

            return new ProxmoxTerraformAccessReport(
                checks.All(check => check.Allowed) ? "ok" : "error",
                ReadOnly: true,
                checks);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            return new ProxmoxTerraformAccessReport(
                "error",
                ReadOnly: true,
                [new ProxmoxTerraformAccessCheck("Read effective permissions", false, [], [], exception.Message)]);
        }
    }

    private static ProxmoxTerraformAccessCheck MissingConfiguration(string name, string key) =>
        new(name, false, [], [], $"{key} is not configured.");

    private static ProxmoxTerraformAccessCheck CheckPrivileges(
        string name,
        IReadOnlyDictionary<string, HashSet<string>> permissions,
        string targetPath,
        IReadOnlyList<string> requiredPrivileges)
    {
        var matchingPaths = permissions.Keys
            .Where(path => PathApplies(path, targetPath))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var granted = matchingPaths
            .SelectMany(path => permissions[path])
            .ToHashSet(StringComparer.Ordinal);
        var missing = requiredPrivileges.Where(privilege => !granted.Contains(privilege)).ToArray();
        return new ProxmoxTerraformAccessCheck(name, missing.Length == 0, matchingPaths, missing, Error: null);
    }

    private static ProxmoxTerraformAccessCheck CheckAnyPathPrivileges(
        string name,
        IReadOnlyDictionary<string, HashSet<string>> permissions,
        IEnumerable<string> targetPaths,
        IReadOnlyList<string> requiredPrivileges)
    {
        var candidates = targetPaths.Distinct(StringComparer.Ordinal)
            .Select(path => CheckPrivileges(name, permissions, path, requiredPrivileges))
            .ToArray();
        var allowedPaths = candidates.Where(candidate => candidate.Allowed)
            .SelectMany(candidate => candidate.Paths)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (allowedPaths.Length > 0)
        {
            return new ProxmoxTerraformAccessCheck(name, true, allowedPaths, [], Error: null);
        }

        var closest = candidates.OrderBy(candidate => candidate.MissingPrivileges.Count).First();
        return closest with { Paths = closest.Paths.Distinct(StringComparer.Ordinal).ToArray() };
    }

    private static bool PathApplies(string grantedPath, string targetPath) =>
        grantedPath == "/" ||
        grantedPath.Equals(targetPath, StringComparison.Ordinal) ||
        targetPath.StartsWith(grantedPath.TrimEnd('/') + "/", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, HashSet<string>> GetPermissionData(object response)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (response is not JsonElement root ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var path in data.EnumerateObject())
        {
            if (path.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result[path.Name] = path.Value.EnumerateObject()
                .Where(privilege => privilege.Value.ValueKind == JsonValueKind.Number && privilege.Value.GetInt32() != 0)
                .Select(privilege => privilege.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private static IEnumerable<JsonElement> GetData(object response)
    {
        if (response is JsonElement root &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        return [];
    }

    private static SortedDictionary<string, object?> ToDictionary(JsonElement element) =>
        new(
            JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), JsonOptions) ?? [],
            StringComparer.Ordinal);

    private static string GetSortValue(IReadOnlyDictionary<string, object?> item, string key)
    {
        if (!item.TryGetValue(key, out var value) || value is null)
        {
            return string.Empty;
        }

        return value is JsonElement element && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : value.ToString() ?? string.Empty;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(property, out var item) &&
            item.ValueKind == JsonValueKind.String &&
            (value = item.GetString() ?? string.Empty).Length > 0;
    }

    private static bool TryGetInt32(JsonElement element, string property, out int value)
    {
        value = default;
        return element.TryGetProperty(property, out var item) && item.TryGetInt32(out value);
    }
}

public sealed record ProxmoxAccessCheck(string Name, string Path, bool Allowed, string? Error);

public sealed record ProxmoxAccessReport(string Status, bool ReadOnly, IReadOnlyList<ProxmoxAccessCheck> Checks)
{
    public bool AllAllowed => Checks.All(check => check.Allowed);
}

public sealed record ProxmoxTerraformAccessCheck(
    string Name,
    bool Allowed,
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> MissingPrivileges,
    string? Error);

public sealed record ProxmoxTerraformAccessReport(
    string Status,
    bool ReadOnly,
    IReadOnlyList<ProxmoxTerraformAccessCheck> Checks)
{
    public bool AllAllowed => Checks.All(check => check.Allowed);
}
