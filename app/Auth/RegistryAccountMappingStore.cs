using System.Text;
using System.Text.Json.Nodes;
using Azure.Identity;

namespace AlexCrome.Acr.CredentialHelper.Auth;

public sealed class RegistryAccountMappingStore : IRegistryAccountMappingStore
{
    private readonly string _directory;

    public RegistryAccountMappingStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlexCrome.Acr.CredentialHelper"))
    {
    }

    internal RegistryAccountMappingStore(string directory)
    {
        _directory = directory;
    }

    internal string FilePath => Path.Combine(_directory, "accounts.json");

    public async Task<IReadOnlyCollection<string>> List(CancellationToken cancellationToken = default)
    {
        var records = await ReadAsync(cancellationToken);
        return records.Select(record => record.Key).ToArray();
    }

    public async Task<AuthenticationRecord?> Get(string registry, CancellationToken cancellationToken = default)
    {
        var records = await ReadAsync(cancellationToken);
        if (!records.TryGetPropertyValue(RegistryHelper.NormalizeHost(registry), out var record))
        {
            return null;
        }

        if (record is not JsonObject)
        {
            throw new InvalidOperationException("Saved authentication record must be a JSON object.");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(record.ToJsonString()));
        return await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
    }

    public async Task Update(string registry, AuthenticationRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        var records = await ReadAsync(cancellationToken);
        using var serialized = new MemoryStream();
        await record.SerializeAsync(serialized, cancellationToken);
        serialized.Position = 0;
        records[RegistryHelper.NormalizeHost(registry)] = await JsonNode.ParseAsync(serialized, cancellationToken: cancellationToken);
        await WriteAsync(records, cancellationToken);
    }

    public async Task Delete(string registry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = RegistryHelper.NormalizeHost(registry);
        if (!Directory.Exists(_directory))
        {
            return;
        }

        var records = await ReadAsync(cancellationToken);
        if (records.Remove(key))
        {
            await WriteAsync(records, cancellationToken);
        }
    }

    private Task WriteAsync(JsonObject records, CancellationToken cancellationToken) =>
        FileHelpers.AtomicallyWriteJson(FilePath, records, cancellationToken);

    private async Task<JsonObject> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new JsonObject();
        }

        var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Authentication record store must be a JSON object.");
    }
}
