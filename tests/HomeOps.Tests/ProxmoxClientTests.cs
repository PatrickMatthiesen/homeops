using System.Net;
using HomeOps.Cli.Infrastructure;
using HomeOps.Cli.Proxmox;

namespace HomeOps.Tests;

public sealed class ProxmoxClientTests
{
    [Fact]
    public async Task SendsProxmoxApiTokenAuthorizationHeader()
    {
        const string token = "terraform@pve!terraform=00000000-0000-0000-0000-000000000000";
        var handler = new CapturingHandler();
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", token);
        var client = new ProxmoxClient(credentials, handler);

        await client.GetAsync("nodes");

        Assert.Equal($"PVEAPIToken={token}", handler.Authorization);
        Assert.Equal("https://proxmox.example:8006/api2/json/nodes", handler.RequestUri?.AbsoluteUri);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.GetValues("Authorization").Single();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        }
    }

    private sealed class ProxmoxCredentialStore(string endpoint, string token) : ICredentialStore
    {
        public string? Get(string name) => name switch
        {
            CredentialKeys.ProxmoxEndpoint => endpoint,
            CredentialKeys.ProxmoxInspectToken => token,
            _ => null
        };

        public void Set(string name, string secret) { }
        public void Delete(string name) { }
        public IReadOnlyDictionary<string, bool> ListMetadata(IEnumerable<string> names) =>
            names.ToDictionary(name => name, _ => true);
    }
}
