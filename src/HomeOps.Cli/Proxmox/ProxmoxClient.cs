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
    {
        var endpoint = credentials.Get(CredentialKeys.ProxmoxEndpoint) ?? throw new InvalidOperationException("Missing proxmox.endpoint.");
        var token = credentials.Get(CredentialKeys.ProxmoxInspectToken) ?? throw new InvalidOperationException("Missing proxmox.inspect.token.");
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
}
