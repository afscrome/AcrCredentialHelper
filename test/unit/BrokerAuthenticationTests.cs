using System.Net;
using Azure.Core;
using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using static TUnit.Mocks.Arguments.Arg;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class BrokerAuthenticationTests
{
    [Test]
    public async Task UnavailableCredentialIsNotReplacedByBroker()
    {
        var credential = DelegatedTokenCredential.Create(
            (_, _) => throw new CredentialUnavailableException("Unavailable"));
        var session = CreateSession(credential);
        await Assert.That(async () => await session.GetTokenAsync()).Throws<CredentialUnavailableException>();
    }

    [Test]
    public async Task TokenCredentialWithoutAccountMetadataReturnsNoRecord()
    {
        var credential = DelegatedTokenCredential.Create((_, _) => new AccessToken("default-token", DateTimeOffset.MaxValue));
        var store = new InMemoryRegistryAccountMappingStore();
        var session = CreateSession(credential, store);
        var token = await session.GetTokenAsync();
        await Assert.That(token.Token).IsEqualTo("default-token");
        await Assert.That(await session.RememberForAsync(["registry.example.com"])).IsEqualTo(0);
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    [Arguments(200, """{"refresh_token":"acr-token"}""", true)]
    [Arguments(200, """{"refresh_token":""}""", false)]
    [Arguments(200, """{"refresh_token":null}""", false)]
    [Arguments(200, """{}""", false)]
    [Arguments(403, """{"errors":[{"code":"DENIED","message":"Access denied"}]}""", false)]
    public async Task ResolverPersistsSelectedAccountOnlyAfterSuccessfulExchange(int status, string body, bool succeeds)
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var broker = new StubBrokerCredential { RequiresInteraction = true };
        using var http = new HttpClient(new ExchangeHandler(status, body));
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => broker);
        var resolver = new AcrCredentialResolver(authenticator, http);
        if (succeeds)
        {
            var result = await resolver.ResolveAsync("registry.example.com");
            await Assert.That(result).IsEqualTo("acr-token");
            await Assert.That((await store.Get("registry.example.com"))!.HomeAccountId).IsEqualTo("user.tenant");
        }
        else if (status == 403)
        {
            await Assert.That(async () => await resolver.ResolveAsync("registry.example.com"))
                .Throws<Azure.RequestFailedException>();
        }
        else
        {
            await Assert.That(async () => await resolver.ResolveAsync("registry.example.com"))
                .Throws<InvalidOperationException>();
        }

        await Assert.That(store.Records.Count).IsEqualTo(succeeds ? 1 : 0);
    }

    private sealed class ExchangeHandler(int status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionRemembersBrokerRecordForMultipleRegistries(bool requiresInteraction)
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var credential = new StubBrokerCredential { RequiresInteraction = requiresInteraction };
        var session = CreateSession(credential, store);
        await session.GetTokenAsync();
        await Assert.That(credential.InteractiveCalls).IsEqualTo(1);
        await Assert.That(await session.RememberForAsync(["first.azurecr.io", "second.azurecr.io"])).IsEqualTo(2);
        await Assert.That(store.Records["first.azurecr.io"].HomeAccountId).IsEqualTo("user.tenant");
        await Assert.That(store.Records["second.azurecr.io"].HomeAccountId).IsEqualTo("user.tenant");
    }

    [Test]
    public async Task SilentSuccessDoesNotSaveAccountMetadata()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var credential = new StubBrokerCredential();
        await CreateSession(credential, store).GetTokenAsync();
        await Assert.That(credential.InteractiveCalls).IsEqualTo(1);
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task AuthenticationFailureDoesNotSaveAccountMetadata()
    {
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Throws(new AuthenticationFailedException("Broker failure"));
        var store = new InMemoryRegistryAccountMappingStore();
        await Assert.That(async () =>
            await CreateSession(credential.Object, store).GetTokenAsync())
            .Throws<AuthenticationFailedException>();
        credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(store.Records).IsEmpty();
    }

    private static AuthenticationSession CreateSession(TokenCredential credential, IRegistryAccountMappingStore? store = null) =>
        new(credential, "tenant", store ?? new InMemoryRegistryAccountMappingStore(), NullLogger.Instance);

}
