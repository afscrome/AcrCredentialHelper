using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Auth;

public sealed class RegistryAuthenticator
{
    private readonly IRegistryAccountMappingStore _recordStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistryAuthenticator> _logger;
    private readonly Func<string?, AuthenticationRecord?, TokenCredential> _buildCredential;

    // Configuration is read per session because command-line options are applied after construction.
    public RegistryAuthenticator(IRegistryAccountMappingStore recordStore, IConfiguration configuration,
        ILoggerFactory loggerFactory, Func<string?, AuthenticationRecord?, TokenCredential>? buildCredential = null)
    {
        _recordStore = recordStore;
        _configuration = configuration;
        _logger = loggerFactory.CreateLogger<RegistryAuthenticator>();
        _buildCredential = buildCredential ?? new TokenCredentialFactory().BuildInteractive;
    }

    public async Task<AuthenticationSession> ForRegistryAsync(string registry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var host = RegistryHelper.NormalizeHost(registry);
        var record = await _recordStore.Get(host, cancellationToken);
        var tenantId = record?.TenantId ?? TenantHelper.GetTenant(_configuration);
        if (record is not null)
        {
            Logs.ExistingAccountIdentified(_logger, record.Username, host);
        }

        return CreateSession(tenantId, record);
    }

    public AuthenticationSession ForConfiguredTenant() => CreateSession(TenantHelper.GetTenant(_configuration), null);

    public AuthenticationSession ForTenant(string? tenantId = null) => CreateSession(tenantId, null);

    public Task ForgetAsync(string registry, CancellationToken cancellationToken = default) =>
        _recordStore.Delete(registry, cancellationToken);

    public Task<IReadOnlyCollection<string>> ListRegistriesAsync(CancellationToken cancellationToken = default) =>
        _recordStore.List(cancellationToken);

    private AuthenticationSession CreateSession(string? tenantId, AuthenticationRecord? record) =>
        new(_buildCredential(tenantId, record), tenantId, _recordStore, _logger, record,
            AuthenticationTimeout.Get(_configuration));

}
