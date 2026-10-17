using Azure.Identity;

namespace AlexCrome.Acr.CredentialHelper.Auth;

public interface IRegistryAccountMappingStore
{
    Task<AuthenticationRecord?> Get(string registry, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> List(CancellationToken cancellationToken = default);
    Task Update(string registry, AuthenticationRecord record, CancellationToken cancellationToken = default);
    Task Delete(string registry, CancellationToken cancellationToken = default);
}
