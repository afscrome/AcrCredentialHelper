using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using MsalClientException = Microsoft.Identity.Client.MsalClientException;
using MsalError = Microsoft.Identity.Client.MsalError;

namespace AlexCrome.Acr.CredentialHelper.Auth;

/// <summary>
/// Owns one operation's authentication state. Account metadata is persisted only
/// by RememberForAsync; concurrent operations must use separate sessions.
/// </summary>
public sealed class AuthenticationSession
{
    private readonly TokenCredential _credential;
    private readonly IRegistryAccountMappingStore _recordStore;
    private readonly ILogger _logger;
    private readonly string? _requestedTenantId;
    private readonly TimeSpan _timeout;
    private AuthenticationRecord? _record;
    private AuthenticationRecord? _selectedAccount;
    private bool _authenticated;

    internal AuthenticationSession(TokenCredential credential, string? tenantId,
        IRegistryAccountMappingStore recordStore, ILogger logger, AuthenticationRecord? record = null,
        TimeSpan? timeout = null)
    {
        _credential = credential;
        _requestedTenantId = tenantId;
        _recordStore = recordStore;
        _logger = logger;
        _record = record;
        _timeout = timeout ?? Timeout.InfiniteTimeSpan;
    }

    public string? TenantId => _record?.TenantId ?? _requestedTenantId;

    /// <exception cref="OperationCanceledException">The user cancels broker sign-in.</exception>
    /// <exception cref="TimeoutException">Authentication does not complete within the configured timeout.</exception>
    public async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _authenticated = false;
        _selectedAccount = _record;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            // WaitAsync guarantees the timeout even if a broker prompt ignores cancellation.
            var token = await AcquireTokenAsync(timeout.Token).WaitAsync(timeout.Token);
            _record = _selectedAccount;
            _authenticated = true;
            Logs.EntraAuthenticationSucceeded(_logger);
            return token;
        }
        catch (Exception ex) when (ex is OperationCanceledException or AuthenticationFailedException
            && timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _selectedAccount = null;
            Logs.AuthenticationTimedOut(_logger, _timeout);
            throw new TimeoutException(
                $"Authentication did not complete within {_timeout}. Set ACR_CREDENTIAL_HELPER_{AuthenticationTimeout.Key} to allow longer.", ex);
        }
        catch (AuthenticationFailedException ex) when (IsAuthenticationCancelled(ex))
        {
            _selectedAccount = null;
            Logs.AuthenticationCancelled(_logger);
            throw new OperationCanceledException("Authentication cancelled.", ex, cancellationToken);
        }
    }

    private async Task<AccessToken> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        var context = new TokenRequestContext(["https://management.azure.com/.default"], tenantId: TenantId);
        try
        {
            var token = await _credential.GetTokenAsync(context, cancellationToken);
            if (_selectedAccount is null && _credential is InteractiveBrowserCredential broker)
            {
                _selectedAccount = await broker.AuthenticateAsync(context, cancellationToken);
            }

            return token;
        }
        catch (AuthenticationRequiredException) when (_credential is InteractiveBrowserCredential)
        {
            var broker = (InteractiveBrowserCredential)_credential;
            Logs.BrokerInteractionRequired(_logger);
            _selectedAccount = await broker.AuthenticateAsync(context, cancellationToken);
            var token = await broker.GetTokenAsync(context, cancellationToken);
            return token;
        }
    }

    private static bool IsAuthenticationCancelled(AuthenticationFailedException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is MsalClientException { ErrorCode: MsalError.AuthenticationCanceledError })
            {
                return true;
            }
        }

        return false;
    }

    public async Task<int> RememberForAsync(IReadOnlyCollection<string> registries,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_authenticated)
        {
            throw new InvalidOperationException("Authenticate successfully before remembering the selected account.");
        }

        if (_selectedAccount is { } record)
        {
            foreach (var registry in registries)
            {
                await _recordStore.Update(registry, record, cancellationToken);
            }

            return registries.Count;
        }

        return 0;
    }

}
