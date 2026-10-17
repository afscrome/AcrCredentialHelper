using Azure.Containers.ContainerRegistry;
using Azure.Core;
using Azure.Core.Pipeline;
using AlexCrome.Acr.CredentialHelper.Auth;

namespace AlexCrome.Acr.CredentialHelper.Acr;

/// <summary>
/// Coordinates authentication and ACR token exchange, remembering the selected
/// account only after the exchange succeeds.
/// </summary>
public sealed class AcrCredentialResolver
{
    public const string AcrUsername = "00000000-0000-0000-0000-000000000000";

    private readonly RegistryAuthenticator _authenticator;
    private readonly HttpClient _httpClient;

    public AcrCredentialResolver(RegistryAuthenticator authenticator, HttpClient httpClient)
    {
        _authenticator = authenticator;
        _httpClient = httpClient;
    }

    /// <returns>The ACR refresh token.</returns>
    /// <exception cref="OperationCanceledException">Authentication is cancelled.</exception>
    public async Task<string> ResolveAsync(string serverUrl, CancellationToken cancellationToken = default)
    {
        var host = RegistryHelper.NormalizeHost(serverUrl);
        var session = await _authenticator.ForRegistryAsync(host, cancellationToken);
        var token = await session.GetTokenAsync(cancellationToken);

        var refreshToken = await TokenExchange(host, session.TenantId, token, cancellationToken);
        await session.RememberForAsync([host], cancellationToken);
        return refreshToken;
    }

    private async Task<string> TokenExchange(string host, string? tenantId,
        AccessToken token, CancellationToken cancellationToken)
    {
        var registryClient = new ContainerRegistryClient(new Uri($"https://{host}"),
            new ContainerRegistryClientOptions { Transport = new HttpClientTransport(_httpClient) });
        var response = await registryClient.ExchangeAadAccessTokenForAcrRefreshTokenAsync(
            service: host, tenant: tenantId, accessToken: token.Token, cancellationToken: cancellationToken);
        var refreshToken = response.Value.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException($"ACR token exchange for '{host}' did not return a refresh token.");
        }

        return refreshToken;
    }
}
