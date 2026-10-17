using System.CommandLine;
using System.Net;
using Azure.Core;
using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using static TUnit.Mocks.Arguments.Arg;
using MsalClientException = Microsoft.Identity.Client.MsalClientException;
using MsalError = Microsoft.Identity.Client.MsalError;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class InstallCommandTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Test]
    public async Task AuthenticationRejectsInvalidTenantBeforeDiscovery()
    {
        using var configuration = new ConfigurationManager
        {
            ["TenantId"] = "invalid",
        };
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Throws(new AuthenticationFailedException("Invalid tenant."));
        var authenticator = new RegistryAuthenticator(new InMemoryRegistryAccountMappingStore(), configuration, NullLoggerFactory.Instance,
            (_, _) => credential.Object);
        using var http = new HttpClient(new Handler(_ =>
            throw new InvalidOperationException("Invalid input must not reach discovery.")));
        var updater = new StubDockerConfigUpdater();
        var command = new InstallCommand(http, NullLoggerFactory.Instance,
            new Option<string>("--tenant"), authenticator, updater, () => true);

        await Assert.That(async () => await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
        })).Throws<AuthenticationFailedException>().WithMessage("Invalid tenant.");
        await Assert.That(updater.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments("success")]
    [Arguments("empty")]
    [Arguments("discovery-failure")]
    [Arguments("configuration-failure")]
    [Arguments("default-account")]
    [Arguments("tenant-domain")]
    public async Task RemembersDiscoveredAccountsOnlyAfterSuccessfulConfiguration(string scenario)
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var returnedRecord = await StubBrokerCredential.ReadRecordAsync(tenantId: Tenant);
        string? credentialTenant = null;
        AuthenticationRecord? suppliedRecord = null;
        var updater = new StubDockerConfigUpdater
        {
            Register = async _ =>
            {
                await Assert.That(store.Records).IsEmpty();
                if (scenario == "configuration-failure")
                {
                    throw new InvalidOperationException("Docker configuration failed.");
                }
            },
        };
        using var configuration = new ConfigurationManager
        {
            ["TenantId"] = scenario switch
            {
                "default-account" => string.Empty,
                "tenant-domain" => "contoso.onmicrosoft.com",
                _ => Tenant,
            },
        };
        var authenticator = new RegistryAuthenticator(store, configuration, NullLoggerFactory.Instance,
            (tenant, record) =>
            {
                credentialTenant = tenant;
                suppliedRecord = record;
                return new StubBrokerCredential(returnedRecord) { RequiresInteraction = true };
            });
        using var http = new HttpClient(new Handler(async request =>
        {
            await Assert.That(request.Headers.Authorization?.Parameter).IsEqualTo("secret-access-token");
            await Assert.That(store.Records).IsEmpty();
            if (request.RequestUri!.AbsolutePath == "/tenants")
            {
                return JsonResponse($$"""{"value":[{"tenantId":"22222222-2222-2222-2222-222222222222"},{"id":"/tenants/{{Tenant}}","tenantId":"{{Tenant}}"}]}""");
            }

            await Assert.That(request.RequestUri.AbsolutePath).IsEqualTo("/providers/Microsoft.ResourceGraph/resources");
            if (scenario == "discovery-failure")
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return JsonResponse(scenario == "empty"
                ? """{"data":[],"resultTruncated":"false"}"""
                : """{"data":[{"loginServer":"first.azurecr.io"},{"loginServer":"second.azurecr.io"}],"resultTruncated":"false"}""");
        }));
        using var output = new StringWriter();
        var command = new InstallCommand(http, NullLoggerFactory.Instance,
            new Option<string>("--tenant"), authenticator, updater, () => true);
        var invocation = new InvocationConfiguration { Output = output, EnableDefaultExceptionHandler = false };

        if (scenario == "discovery-failure")
        {
            await Assert.That(async () => await command.Parse([]).InvokeAsync(invocation))
                .Throws<Azure.RequestFailedException>();
        }
        else if (scenario == "configuration-failure")
        {
            await Assert.That(async () => await command.Parse([]).InvokeAsync(invocation))
                .Throws<InvalidOperationException>().WithMessage("Docker configuration failed.");
        }
        else
        {
            await Assert.That(await command.Parse([]).InvokeAsync(invocation)).IsEqualTo(0);
        }

        await Assert.That(credentialTenant).IsEqualTo(scenario switch
        {
            "default-account" => null,
            "tenant-domain" => "contoso.onmicrosoft.com",
            _ => Tenant,
        });
        await Assert.That(suppliedRecord).IsNull();
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
        await Assert.That(updater.Calls).IsEqualTo(
            scenario is "success" or "default-account" or "tenant-domain" or "configuration-failure" ? 1 : 0);
        if (scenario is "success" or "default-account" or "tenant-domain")
        {
            await Assert.That(store.Records.Count).IsEqualTo(2);
            foreach (var host in new[] { "first.azurecr.io", "second.azurecr.io" })
            {
                await Assert.That(store.Records[host].HomeAccountId).IsEqualTo("user.tenant");
                await Assert.That(store.Records[host].TenantId).IsEqualTo(Tenant);
            }

            await Assert.That(updater.RegisteredHosts)
                .IsEquivalentTo(new[] { "first.azurecr.io", "second.azurecr.io" });
        }
        else
        {
            await Assert.That(store.Records).IsEmpty();
            await Assert.That(updater.RegisteredHosts).IsEmpty();
        }
    }

    [Test]
    [Arguments("token", true)]
    [Arguments("automatic-interaction", true)]
    [Arguments("explicit-interaction", true)]
    [Arguments("token", false)]
    [Arguments("explicit-interaction", false)]
    public async Task BrokerCancellationThrowsCancellationButOtherAuthenticationFailuresPropagate(string stage, bool cancelled)
    {
        var failure = new AuthenticationFailedException("User canceled authentication.",
            new InvalidOperationException("Broker wrapper", new MsalClientException(
                cancelled ? MsalError.AuthenticationCanceledError : MsalError.AuthenticationFailed)));
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        if (stage == "token")
        {
            credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>()).Throws(failure);
        }
        else
        {
            if (stage == "explicit-interaction")
            {
                credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
                    .Throws(new AuthenticationRequiredException("Sign in required",
                        new TokenRequestContext(["https://management.azure.com/.default"])));
            }
            else
            {
                credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
                    .Returns(new AccessToken("token", DateTimeOffset.MaxValue));
            }

            credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>()).Throws(failure);
        }
        var store = new InMemoryRegistryAccountMappingStore();
        using var configuration = new ConfigurationManager
        {
            ["TenantId"] = Tenant,
        };
        var authenticator = new RegistryAuthenticator(store, configuration, NullLoggerFactory.Instance,
            (_, _) => credential.Object);
        using var http = new HttpClient(new Handler(_ =>
            throw new InvalidOperationException("Failed authentication must not reach discovery.")));
        using var output = new StringWriter();
        var updater = new StubDockerConfigUpdater();
        var command = new InstallCommand(http, NullLoggerFactory.Instance,
            new Option<string>("--tenant"), authenticator, updater, () => true);
        var invocation = new InvocationConfiguration { Output = output, EnableDefaultExceptionHandler = false };

        if (cancelled)
        {
            await Assert.That(async () => await command.Parse([]).InvokeAsync(invocation))
                .Throws<OperationCanceledException>();
        }
        else
        {
            await Assert.That(async () => await command.Parse([]).InvokeAsync(invocation))
                .Throws<AuthenticationFailedException>().WithMessage(failure.Message);
        }

        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
        await Assert.That(store.Records).IsEmpty();
        await Assert.That(updater.Calls).IsEqualTo(0);
        credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .WasCalled(stage == "token" ? Times.Never : Times.Once);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }

    private sealed class StubDockerConfigUpdater : IDockerConfigUpdater
    {
        internal Func<IReadOnlyCollection<string>, Task>? Register { get; init; }
        internal int Calls { get; private set; }
        internal IReadOnlyCollection<string> RegisteredHosts { get; private set; } = [];

        public async Task<IReadOnlyCollection<string>> RegisterRegistriesAsync(IReadOnlyCollection<string> hosts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Register is { } register)
            {
                await register(hosts);
            }

            RegisteredHosts = hosts.ToArray();
            return RegisteredHosts;
        }
    }
}
