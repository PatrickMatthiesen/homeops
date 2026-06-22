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

    [Fact]
    public async Task VirtualMachineInventoryIncludesTemplatesAndExcludesOtherResources()
    {
        const string resources = """
            {"data":[
              {"type":"qemu","vmid":101,"name":"application","template":0},
              {"type":"lxc","vmid":102,"name":"container"},
              {"type":"storage","storage":"local"}
            ]}
            """;
        var handler = new RoutingHandler(new Dictionary<string, string>
        {
            ["cluster/resources"] = resources,
            ["nodes"] = "{\"data\":[{\"node\":\"example-node\"}]}",
            ["nodes/example-node/qemu"] = "{\"data\":[{\"vmid\":100,\"name\":\"debian-13-template\",\"template\":1},{\"vmid\":101,\"name\":\"application\",\"template\":0}]}",
            ["nodes/example-node/lxc"] = "{\"data\":[{\"vmid\":102,\"name\":\"container\"}]}"
        });
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "synthetic-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.GetVirtualMachinesAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("debian-13-template", json);
        Assert.Contains("application", json);
        Assert.Contains("container", json);
        Assert.DoesNotContain("local", json);
        Assert.Equal(4, handler.RequestUris.Count);
        Assert.Contains(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/nodes/example-node/qemu", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StorageContentIncludesNodeAndStorageContext()
    {
        var handler = new RoutingHandler(new Dictionary<string, string>
        {
            ["nodes"] = "{\"data\":[{\"node\":\"example-node\"}]}",
            ["nodes/example-node/storage"] = "{\"data\":[{\"storage\":\"local\"},{\"storage\":\"vm-storage\"}]}",
            ["nodes/example-node/storage/local/content"] = "{\"data\":[{\"volid\":\"local:iso/debian.iso\",\"content\":\"iso\"}]}",
            ["nodes/example-node/storage/vm-storage/content"] = "{\"data\":[{\"volid\":\"vm-storage:vm-101-disk-0\",\"content\":\"images\"}]}"
        });
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "synthetic-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.GetStorageContentAsync();
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Contains("debian.iso", json);
        Assert.Contains("vm-101-disk-0", json);
        Assert.Contains("example-node", json);
        Assert.Contains("vm-storage", json);
        Assert.Equal(4, handler.RequestUris.Count);
    }

    [Fact]
    public async Task StorageContentCanFilterByTypeAndHasDeterministicOrdering()
    {
        var handler = new RoutingHandler(new Dictionary<string, string>
        {
            ["nodes"] = "{\"data\":[{\"node\":\"example-node\"}]}",
            ["nodes/example-node/storage"] = "{\"data\":[{\"storage\":\"local\"}]}",
            ["nodes/example-node/storage/local/content"] = "{\"data\":[{\"size\":2,\"volid\":\"local:iso/z.iso\",\"content\":\"iso\"},{\"content\":\"backup\",\"volid\":\"local:backup/a.zst\"},{\"volid\":\"local:iso/a.iso\",\"content\":\"iso\",\"size\":1}]}"
        });
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "synthetic-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.GetStorageContentAsync("ISO");
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.DoesNotContain("backup", json);
        Assert.True(json.IndexOf("a.iso", StringComparison.Ordinal) < json.IndexOf("z.iso", StringComparison.Ordinal));
        var firstObject = json[json.IndexOf("{\"content\"", StringComparison.Ordinal)..];
        Assert.True(firstObject.IndexOf("\"content\"", StringComparison.Ordinal) < firstObject.IndexOf("\"node\"", StringComparison.Ordinal));
        Assert.True(firstObject.IndexOf("\"node\"", StringComparison.Ordinal) < firstObject.IndexOf("\"size\"", StringComparison.Ordinal));
        Assert.True(firstObject.IndexOf("\"size\"", StringComparison.Ordinal) < firstObject.IndexOf("\"storage\"", StringComparison.Ordinal));
        Assert.True(firstObject.IndexOf("\"storage\"", StringComparison.Ordinal) < firstObject.IndexOf("\"volid\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InspectionAccessCheckOnlyReadsRequiredEndpoints()
    {
        var handler = new AccessHandler(new Dictionary<string, HttpStatusCode>
        {
            ["cluster/resources"] = HttpStatusCode.OK,
            ["nodes"] = HttpStatusCode.OK,
            ["storage"] = HttpStatusCode.OK,
            ["nodes/example-node/qemu"] = HttpStatusCode.OK,
            ["nodes/example-node/lxc"] = HttpStatusCode.OK,
            ["nodes/example-node/storage"] = HttpStatusCode.OK,
            ["nodes/example-node/storage/local/content"] = HttpStatusCode.OK
        });
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "synthetic-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckInspectionAccessAsync();

        Assert.True(result.ReadOnly);
        Assert.True(result.AllAllowed);
        Assert.Equal("ok", result.Status);
        Assert.Equal(7, result.Checks.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task InspectionAccessCheckReportsDeniedEndpointAndContinues()
    {
        var handler = new AccessHandler(new Dictionary<string, HttpStatusCode>
        {
            ["cluster/resources"] = HttpStatusCode.OK,
            ["nodes"] = HttpStatusCode.OK,
            ["storage"] = HttpStatusCode.Forbidden,
            ["nodes/example-node/qemu"] = HttpStatusCode.OK,
            ["nodes/example-node/lxc"] = HttpStatusCode.OK,
            ["nodes/example-node/storage"] = HttpStatusCode.OK,
            ["nodes/example-node/storage/local/content"] = HttpStatusCode.OK
        });
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "synthetic-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckInspectionAccessAsync();

        Assert.False(result.AllAllowed);
        Assert.Equal("error", result.Status);
        var storage = Assert.Single(result.Checks, check => check.Path == "storage");
        Assert.False(storage.Allowed);
        Assert.Contains("403", storage.Error);
        Assert.Equal(7, result.Checks.Count);
    }

    [Fact]
    public async Task TerraformAccessCheckUsesTerraformTokenAndOnlyReadsPermissions()
    {
        const string permissions = """
            {"data":{
              "/":{"Sys.Audit":1,"Sys.AccessNetwork":1},
              "/vms":{"VM.Allocate":1,"VM.Audit":1,"VM.Clone":1,"VM.Config.CDROM":1,"VM.Config.Cloudinit":1,"VM.Config.CPU":1,"VM.Config.Disk":1,"VM.Config.HWType":1,"VM.Config.Memory":1,"VM.Config.Network":1,"VM.Config.Options":1,"VM.PowerMgmt":1},
              "/storage/local":{"Datastore.AllocateSpace":1,"Datastore.AllocateTemplate":1,"Datastore.Audit":1},
              "/storage/vm-storage":{"Datastore.AllocateSpace":1,"Datastore.Audit":1}
            }}
            """;
        var handler = new CapturingHandler(permissions);
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "inspect-token", "terraform-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckTerraformAccessAsync("example-node", "local", cloudImageDownloads: true);

        Assert.True(result.ReadOnly);
        Assert.True(result.AllAllowed);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("PVEAPIToken=terraform-token", handler.Authorization);
        Assert.Equal("https://proxmox.example:8006/api2/json/access/permissions", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task TerraformAccessCheckReportsMissingPrivilegesWithoutExposingPermissionDocument()
    {
        var handler = new CapturingHandler("{\"data\":{\"/\":{\"Sys.Audit\":1}}}");
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "inspect-token", "terraform-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckTerraformAccessAsync("example-node", "local", cloudImageDownloads: true);

        Assert.False(result.AllAllowed);
        var vm = Assert.Single(result.Checks, check => check.Name == "Manage virtual machines");
        Assert.Contains("VM.Allocate", vm.MissingPrivileges);
        var storage = Assert.Single(result.Checks, check => check.Name == "Allocate storage");
        Assert.Contains("Datastore.AllocateSpace", storage.MissingPrivileges);
    }

    [Fact]
    public async Task TerraformAccessCheckDoesNotCombinePrivilegesFromDifferentVmPaths()
    {
        const string permissions = """
            {"data":{
              "/":{"Sys.Audit":1,"Sys.AccessNetwork":1},
              "/vms/101":{"VM.Allocate":1,"VM.Audit":1,"VM.Clone":1,"VM.Config.CDROM":1,"VM.Config.Cloudinit":1,"VM.Config.CPU":1},
              "/vms/102":{"VM.Config.Disk":1,"VM.Config.HWType":1,"VM.Config.Memory":1,"VM.Config.Network":1,"VM.Config.Options":1,"VM.PowerMgmt":1},
              "/storage/vm-storage":{"Datastore.AllocateSpace":1,"Datastore.AllocateTemplate":1,"Datastore.Audit":1}
            }}
            """;
        var handler = new CapturingHandler(permissions);
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "inspect-token", "terraform-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckTerraformAccessAsync("example-node", "vm-storage", cloudImageDownloads: true);

        var vm = Assert.Single(result.Checks, check => check.Name == "Manage virtual machines");
        Assert.False(vm.Allowed);
        Assert.Contains("VM.Allocate", vm.MissingPrivileges);
    }

    [Fact]
    public async Task TerraformAccessCheckOmitsCloudImageChecksWhenFeatureIsDisabled()
    {
        const string permissions = """
            {"data":{
              "/":{"Sys.Audit":1},
              "/vms":{"VM.Allocate":1,"VM.Audit":1,"VM.Clone":1,"VM.Config.CDROM":1,"VM.Config.Cloudinit":1,"VM.Config.CPU":1,"VM.Config.Disk":1,"VM.Config.HWType":1,"VM.Config.Memory":1,"VM.Config.Network":1,"VM.Config.Options":1,"VM.PowerMgmt":1},
              "/storage/vm-storage":{"Datastore.AllocateSpace":1,"Datastore.Audit":1}
            }}
            """;
        var handler = new CapturingHandler(permissions);
        var credentials = new ProxmoxCredentialStore("https://proxmox.example:8006", "inspect-token", "terraform-token");
        var client = new ProxmoxClient(credentials, handler);

        var result = await client.CheckTerraformAccessAsync("", "", cloudImageDownloads: false);

        Assert.True(result.AllAllowed);
        Assert.DoesNotContain(result.Checks, check => check.Name.Contains("cloud images", StringComparison.Ordinal));
    }

    private sealed class RoutingHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            RequestUris.Add(uri);
            var path = uri.AbsolutePath.Split("/api2/json/", 2, StringSplitOptions.None)[1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses[path])
            });
        }
    }

    private sealed class CapturingHandler(string response = "{\"data\":[]}") : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.GetValues("Authorization").Single();
            RequestUri = request.RequestUri;
            Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }

    private sealed class AccessHandler(IReadOnlyDictionary<string, HttpStatusCode> statuses) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            var path = request.RequestUri!.AbsolutePath.Split("/api2/json/", 2, StringSplitOptions.None)[1];
            var content = path switch
            {
                "nodes" => "{\"data\":[{\"node\":\"example-node\"}]}",
                "nodes/example-node/storage" => "{\"data\":[{\"storage\":\"local\"}]}",
                _ => "{\"data\":[]}"
            };
            return Task.FromResult(new HttpResponseMessage(statuses[path])
            {
                Content = new StringContent(content)
            });
        }
    }

    private sealed class ProxmoxCredentialStore(string endpoint, string token, string? terraformToken = null) : ICredentialStore
    {
        public string? Get(string name) => name switch
        {
            CredentialKeys.ProxmoxEndpoint => endpoint,
            CredentialKeys.ProxmoxInspectToken => token,
            CredentialKeys.ProxmoxTerraformToken => terraformToken,
            _ => null
        };

        public void Set(string name, string secret) { }
        public void Delete(string name) { }
        public IReadOnlyDictionary<string, bool> ListMetadata(IEnumerable<string> names) =>
            names.ToDictionary(name => name, _ => true);
    }
}
