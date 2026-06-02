using System.Net.Http.Headers;
using System.Text.Json;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Security;

namespace HomeOps.Cli.Proxmox;

public sealed class ProxmoxClient(ICredentialStore credentials)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<object> GetAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var endpoint = credentials.Get(CredentialKeys.ProxmoxEndpoint) ?? throw new InvalidOperationException("Missing proxmox.endpoint.");
        var token = credentials.Get(CredentialKeys.ProxmoxInspectToken) ?? throw new InvalidOperationException("Missing proxmox.inspect.token.");
        using var client = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("PVEAPIToken", token);
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
