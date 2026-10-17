using Azure.Core;
using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using static AlexCrome.Acr.CredentialHelper.Tests.StubBrokerCredential;
using static TUnit.Mocks.Arguments.Arg;
using MsalClientException = Microsoft.Identity.Client.MsalClientException;
using MsalError = Microsoft.Identity.Client.MsalError;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class AuthenticationWorkflowTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnspecifiedTenantIsResolvedFromAuthenticatedAccount(bool requiresInteraction)
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var record = await ReadRecordAsync(tenantId: Tenant);
        var broker = new StubBrokerCredential(record) { RequiresInteraction = requiresInteraction };
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (tenant, _) =>
            {
                if (tenant is not null)
                {
                    throw new InvalidOperationException("An unspecified tenant must remain unset for broker authentication.");
                }

                return broker;
            });
        var session = authenticator.ForTenant();

        await Assert.That(session.TenantId).IsNull();
        await Assert.That((await session.GetTokenAsync()).Token).IsEqualTo("secret-access-token");
        await Assert.That(session.TenantId).IsEqualTo(Tenant);
        await Assert.That(broker.Contexts.All(context => context.TenantId is null)).IsTrue();
        await session.RememberForAsync(["registry.example.com"]);
        await Assert.That((await store.Get("registry.example.com"))!.TenantId).IsEqualTo(Tenant);
        await session.GetTokenAsync();
        await Assert.That(broker.Contexts.Last().TenantId).IsEqualTo(Tenant);
        await Assert.That(broker.InteractiveCalls).IsEqualTo(1);
    }

    [Test]
    public async Task SavedAccountsSupplyTheirOwnTenantsAndErasedAccountsUseDefault()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        await store.Update("first.example.com", await ReadRecordAsync(tenantId: "first-tenant"));
        await store.Update("second.example.com", await ReadRecordAsync(tenantId: "second-tenant"));
        var credentialTenants = new List<string?>();
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (tenant, record) =>
            {
                credentialTenants.Add(tenant);
                return new StubBrokerCredential(record);
            });

        var first = await authenticator.ForRegistryAsync("first.example.com");
        var second = await authenticator.ForRegistryAsync("second.example.com");
        await Assert.That(first.TenantId).IsEqualTo("first-tenant");
        await Assert.That(second.TenantId).IsEqualTo("second-tenant");
        await Assert.That(credentialTenants).IsEquivalentTo(new string?[] { "first-tenant", "second-tenant" });

        await authenticator.ForgetAsync("first.example.com");
        var erased = await authenticator.ForRegistryAsync("first.example.com");
        await Assert.That(erased.TenantId).IsEqualTo(TenantHelper.GetTenant(TestConfiguration.Empty));
        await Assert.That((await store.Get("second.example.com"))!.TenantId).IsEqualTo("second-tenant");
    }

    [Test]
    public async Task FreshAuthenticatorReusesPersistedAccountWithoutInteractiveSignIn()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var firstCredential = new StubBrokerCredential { RequiresInteraction = true };
        var firstAuthenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => firstCredential);
        var firstSession = await firstAuthenticator.ForRegistryAsync("registry.example.com");
        await firstSession.GetTokenAsync();
        await firstSession.RememberForAsync(["registry.example.com"]);
        await Assert.That(firstCredential.InteractiveCalls).IsEqualTo(1);
        StubBrokerCredential? nextCredential = null;
        var nextAuthenticator = new RegistryAuthenticator(store,
            TestConfiguration.Empty, NullLoggerFactory.Instance, (_, record) =>
            {
                if (record is null)
                {
                    throw new InvalidOperationException("The saved account must reach credential construction.");
                }

                nextCredential = new StubBrokerCredential(record);
                return nextCredential;
            });
        var nextSession = await nextAuthenticator.ForRegistryAsync("https://REGISTRY.example.com/v2/");
        await Assert.That((await nextSession.GetTokenAsync()).Token).IsEqualTo("secret-access-token");
        await nextSession.RememberForAsync(["registry.example.com"]);
        await Assert.That((await nextSession.GetTokenAsync()).Token).IsEqualTo("secret-access-token");

        await Assert.That(nextCredential!.InteractiveCalls).IsEqualTo(0);
        await Assert.That((await store.Get("registry.example.com"))!.HomeAccountId)
            .IsEqualTo("user.tenant");
    }

    [Test]
    public async Task SavedAccountPromptsOnlyWhenSilentAcquisitionRequiresInteraction()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        await store.Update("registry.example.com", await ReadRecordAsync("previous@example.com"));
        var credential = new StubBrokerCredential { RequiresInteraction = true };
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => credential);
        var session = await authenticator.ForRegistryAsync("registry.example.com");

        await Assert.That((await session.GetTokenAsync()).Token).IsEqualTo("secret-access-token");
        await Assert.That(credential.InteractiveCalls).IsEqualTo(1);
        await Assert.That((await store.Get("registry.example.com"))!.Username).IsEqualTo("previous@example.com");
        await session.RememberForAsync(["registry.example.com"]);
        await Assert.That((await store.Get("registry.example.com"))!.Username).IsEqualTo("user@example.com");
        await session.GetTokenAsync();
        await Assert.That(credential.InteractiveCalls).IsEqualTo(1);
    }

    [Test]
    public async Task CancelledSessionCannotRememberAnEarlierSelectedAccount()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Returns(new AccessToken("token", DateTimeOffset.MaxValue));
        credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Returns(await ReadRecordAsync());
        var session = new AuthenticationSession(credential.Object, Tenant, store, NullLogger.Instance);
        await session.GetTokenAsync();
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Throws(new AuthenticationFailedException("Cancelled",
                new MsalClientException(MsalError.AuthenticationCanceledError)));

        await Assert.That(async () => await session.GetTokenAsync()).Throws<OperationCanceledException>();
        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"]))
            .Throws<InvalidOperationException>();
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task RegistrySessionCreationDoesNotAuthenticateOrPersistRecords()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance);

        var session = await authenticator.ForRegistryAsync(" https://REGISTRY.example.com/v2/ ");

        await Assert.That(session.TenantId).IsEqualTo(TenantHelper.GetTenant(TestConfiguration.Empty));
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task SeparateSessionsDoNotOverwriteEachOthersSelectedAccount()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var firstRecord = await ReadRecordAsync("first@example.com", "first-tenant");
        var secondRecord = await ReadRecordAsync("second@example.com", "second-tenant");
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (tenant, _) => new StubBrokerCredential(tenant == "first-tenant" ? firstRecord : secondRecord));
        var first = authenticator.ForTenant("first-tenant");
        var second = authenticator.ForTenant("second-tenant");

        await first.GetTokenAsync();
        await second.GetTokenAsync();
        await second.RememberForAsync(["second.example.com"]);
        await first.RememberForAsync(["first.example.com"]);

        await Assert.That((await store.Get("first.example.com"))!.Username).IsEqualTo("first@example.com");
        await Assert.That((await store.Get("second.example.com"))!.Username).IsEqualTo("second@example.com");
    }

    [Test]
    public async Task RegistrySessionHonorsCancellationBeforeResolvingDependencies()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => throw new InvalidOperationException("Must not construct a credential."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await authenticator.ForRegistryAsync("registry.example.com", cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task RememberRequiresSuccessfulAuthentication()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Returns(new AccessToken("token", DateTimeOffset.MaxValue));
        credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Returns(await ReadRecordAsync());
        var session = new AuthenticationSession(credential.Object, Tenant, store, NullLogger.Instance);

        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"]))
            .Throws<InvalidOperationException>();
        await session.GetTokenAsync();
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Throws(new AuthenticationFailedException("Broker failure"));
        await Assert.That(async () => await session.GetTokenAsync()).Throws<AuthenticationFailedException>();
        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"]))
            .Throws<InvalidOperationException>();
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task SessionHonorsCancellationWithoutPersistingAccount()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var credential = new StubBrokerCredential();
        var session = new AuthenticationSession(credential, Tenant, store, NullLogger.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await session.GetTokenAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await session.GetTokenAsync();
        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"], cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task MalformedAccountStoreFailsBeforeCredentialConstruction()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        store.Get("registry.example.com", Any<CancellationToken>())
            .Throws(new InvalidOperationException("Authentication record store must be a JSON object."));
        var authenticator = new RegistryAuthenticator(store.Object, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => throw new InvalidOperationException("Must not construct a credential."));

        await Assert.That(async () => await authenticator.ForRegistryAsync("registry.example.com"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RememberPropagatesPersistenceFailure()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        store.Update("registry.example.com", Any<AuthenticationRecord>(), Any<CancellationToken>())
            .Throws(new InvalidOperationException("Authentication record store must be a JSON object."));
        var session = new AuthenticationSession(new StubBrokerCredential(), Tenant, store.Object, NullLogger.Instance);
        await session.GetTokenAsync();

        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"]))
            .Throws<InvalidOperationException>();
    }
}
