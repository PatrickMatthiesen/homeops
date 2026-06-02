namespace HomeOps.Cli.Infrastructure;

public interface ICredentialStore
{
    string? Get(string name);
    void Set(string name, string secret);
    void Delete(string name);
    IReadOnlyDictionary<string, bool> ListMetadata(IEnumerable<string> names);
}
