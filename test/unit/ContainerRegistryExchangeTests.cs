using System.Net;
using Azure;
using Azure.Core;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class ContainerRegistryExchangeTests
{
    [Test]
    [Arguments("registry.azurecr.io", "tenant-id")]
    [Arguments("registry.example.com", null)]
    public async Task ExchangesEntraTokenUsingSdk(string host, string? tenant)
    {
        var expectedTenant = tenant ?? TenantHelper.GetTenant(TestConfiguration.Empty);
        var calls = 0;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            calls++;
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
            await Assert.That(request.RequestUri!.Host).IsEqualTo(host);
            await Assert.That(request.RequestUri.AbsolutePath).IsEqualTo("/oauth2/exchange");
            var form = (await request.Content!.ReadAsStringAsync()).Split('&')
                .Select(value => value.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
            await Assert.That(form["grant_type"]).IsEqualTo("access_token");
            await Assert.That(form["service"]).IsEqualTo(host);
            await Assert.That(form["access_token"]).IsEqualTo("entra-token");
            if (expectedTenant is not null)
            {
                await Assert.That(form["tenant"]).IsEqualTo(expectedTenant);
            }
            else
            {
                await Assert.That(form.ContainsKey("tenant")).IsFalse();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"refresh_token":"acr-refresh-token"}"""),
            };
        }));

        var resolver = await CreateResolver(client, host, tenant);
        var refreshToken = await resolver.ResolveAsync(host);
        await Assert.That(refreshToken).IsEqualTo("acr-refresh-token");
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task ExchangeReportsServiceFailure()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"errors":[{"code":"DENIED","message":"Access denied"}]}"""),
            })));

        var resolver = await CreateResolver(client, "registry.azurecr.io", "tenant-id");
        await Assert.That(async () => await resolver.ResolveAsync("registry.azurecr.io"))
            .Throws<RequestFailedException>();
    }

    private static async Task<AcrCredentialResolver> CreateResolver(HttpClient client, string host, string? tenant)
    {
        var store = new InMemoryRegistryAccountMappingStore();
        if (tenant is not null)
        {
            await store.Update(host, await StubBrokerCredential.ReadRecordAsync(tenantId: tenant));
        }

        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => DelegatedTokenCredential.Create((_, _) => new AccessToken("entra-token", DateTimeOffset.MaxValue)));
        return new AcrCredentialResolver(authenticator, client);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
