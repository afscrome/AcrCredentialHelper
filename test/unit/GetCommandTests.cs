using System.CommandLine;
using System.Net;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using static TUnit.Mocks.Arguments.Arg;
using MsalClientException = Microsoft.Identity.Client.MsalClientException;
using MsalError = Microsoft.Identity.Client.MsalError;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class GetCommandTests
{
    [Test]
    public async Task UsesSavedAccountTenantAndWritesDockerCredentials()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var savedRecord = await StubBrokerCredential.ReadRecordAsync();
        await store.Update("registry.example.com", savedRecord);
        string? credentialTenant = null;
        AuthenticationRecord? reusedRecord = null;
        StubBrokerCredential? broker = null;
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (tenant, record) =>
            {
                credentialTenant = tenant;
                reusedRecord = record;
                broker = new StubBrokerCredential(record);
                return broker;
            });
        using var http = new HttpClient(new Handler(async request =>
        {
            await Assert.That(request.RequestUri!.Host).IsEqualTo("registry.example.com");
            var form = (await request.Content!.ReadAsStringAsync()).Split('&')
                .Select(value => value.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
            await Assert.That(form["tenant"]).IsEqualTo(savedRecord.TenantId);
            await Assert.That(form["access_token"]).IsEqualTo("secret-access-token");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"refresh_token":"acr-token"}"""),
            };
        }));
        using var input = new StringReader(" https://REGISTRY.example.com/v2/ ");
        using var output = new StringWriter();
        var command = new GetCommand(input, new AcrCredentialResolver(authenticator, http), NullLoggerFactory.Instance);

        var exit = await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            EnableDefaultExceptionHandler = false,
        });

        await Assert.That(exit).IsEqualTo(0);
        var response = JsonNode.Parse(output.ToString())!;
        await Assert.That(response["Username"]!.GetValue<string>()).IsEqualTo(AcrCredentialResolver.AcrUsername);
        await Assert.That(response["Secret"]!.GetValue<string>()).IsEqualTo("acr-token");
        await Assert.That(credentialTenant).IsEqualTo(savedRecord.TenantId);
        await Assert.That(reusedRecord).IsSameReferenceAs(savedRecord);
        await Assert.That(broker!.InteractiveCalls).IsEqualTo(0);
        await Assert.That(broker.Contexts.Single().TenantId).IsEqualTo(savedRecord.TenantId);
        await Assert.That(broker.Contexts.Single().Scopes.Single()).IsEqualTo("https://management.azure.com/.default");
        await Assert.That(store.Records["registry.example.com"].HomeAccountId).IsEqualTo(savedRecord.HomeAccountId);
    }

    [Test]
    public async Task RejectsEmptyInputBeforeAuthentication()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        var authenticator = new RegistryAuthenticator(store.Object, TestConfiguration.Empty, NullLoggerFactory.Instance);
        using var http = new HttpClient(new Handler(_ =>
            throw new InvalidOperationException("Invalid input must not reach exchange.")));
        using var input = new StringReader("   ");
        using var output = new StringWriter();
        var command = new GetCommand(input, new AcrCredentialResolver(authenticator, http), NullLoggerFactory.Instance);

        await Assert.That(async () => await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            EnableDefaultExceptionHandler = false,
        })).Throws<ArgumentException>();
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("token", true)]
    [Arguments("automatic-interaction", true)]
    [Arguments("explicit-interaction", true)]
    [Arguments("token", false)]
    [Arguments("automatic-interaction", false)]
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
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => credential.Object);
        using var http = new HttpClient(new Handler(_ =>
            throw new InvalidOperationException("Failed authentication must not reach exchange.")));
        using var input = new StringReader("registry.example.com");
        using var output = new StringWriter();
        var command = new GetCommand(input, new AcrCredentialResolver(authenticator, http), NullLoggerFactory.Instance);
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
        credential.AuthenticateAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .WasCalled(stage == "token" ? Times.Never : Times.Once);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
