using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Auth;

namespace AlexCrome.Acr.CredentialHelper.Tests;

internal sealed class InMemoryRegistryAccountMappingStore : IRegistryAccountMappingStore
{
    internal Dictionary<string, AuthenticationRecord> Records { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyCollection<string>> List(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<string>>(Records.Keys.ToArray());
    }

    public Task<AuthenticationRecord?> Get(string registry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Records.TryGetValue(RegistryHelper.NormalizeHost(registry), out var record);
        return Task.FromResult(record);
    }

    public Task Update(string registry, AuthenticationRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Records[RegistryHelper.NormalizeHost(registry)] = record;
        return Task.CompletedTask;
    }

    public Task Delete(string registry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Records.Remove(RegistryHelper.NormalizeHost(registry));
        return Task.CompletedTask;
    }
}
