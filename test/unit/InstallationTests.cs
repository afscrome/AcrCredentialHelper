using System.Net;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using AlexCrome.Acr.CredentialHelper.Acr;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class InstallationTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Test]
    public async Task ConfigUpdatePreservesSettingsAndIsIdempotent()
    {
        const string original =
            """{"credsStore":"desktop","auths":{"other.example.com":{"auth":"existing"}},"credHelpers":{"other.example.com":"other","first.azurecr.io":"old"}}""";

        var config = DockerConfigUpdater.CreateUpdatedConfig(original,
            ["https://FIRST.azurecr.io/v2/", "second.azurecr.io"]);
        var repeated = DockerConfigUpdater.CreateUpdatedConfig(config.Config.ToJsonString(),
            ["first.azurecr.io", "second.azurecr.io"]);

        await Assert.That(config.Added).IsEquivalentTo(new[] { "second.azurecr.io" });
        await Assert.That(config.Configured).IsEquivalentTo(new[] { "second.azurecr.io" });
        await Assert.That(config.Conflicts["first.azurecr.io"]).IsEqualTo("old");
        await Assert.That(repeated.Added).IsEmpty();
        await Assert.That(repeated.Configured).IsEquivalentTo(new[] { "second.azurecr.io" });
        var result = repeated.Config;
        await Assert.That(result["credsStore"]!.GetValue<string>()).IsEqualTo("desktop");
        await Assert.That(result["auths"]!["other.example.com"]!["auth"]!.GetValue<string>()).IsEqualTo("existing");
        await Assert.That(result["credHelpers"]!["other.example.com"]!.GetValue<string>()).IsEqualTo("other");
        await Assert.That(result["credHelpers"]!["first.azurecr.io"]!.GetValue<string>()).IsEqualTo("old");
        await Assert.That(result["credHelpers"]!["second.azurecr.io"]!.GetValue<string>()).IsEqualTo("acr");
    }

    [Test]
    public async Task ConfigUpdateCountsExistingAcrEntriesAsConfigured()
    {
        var update = DockerConfigUpdater.CreateUpdatedConfig("""{"credHelpers":{"first.azurecr.io":"acr"}}""",
            ["first.azurecr.io", "registry.example.com:5000"]);

        await Assert.That(update.Added).IsEquivalentTo(new[] { "registry.example.com:5000" });
        await Assert.That(update.Configured).IsEquivalentTo(new[] { "first.azurecr.io", "registry.example.com:5000" });
        await Assert.That(update.Conflicts).IsEmpty();
    }

    [Test]
    [NotInParallel("DOCKER_CONFIG")]
    public async Task RegisterCreatesMissingDockerDirectoryAndSkipsUnchangedWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), $"acr-tests-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(root, "missing", ".docker");
        var previous = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        Environment.SetEnvironmentVariable("DOCKER_CONFIG", dockerDirectory);
        try
        {
            var updater = new DockerConfigUpdater(NullLoggerFactory.Instance);
            var configured = await updater.RegisterRegistriesAsync(["first.azurecr.io"]);
            var path = Path.Combine(dockerDirectory, "config.json");
            await Assert.That(configured).IsEquivalentTo(new[] { "first.azurecr.io" });
            await Assert.That(JsonNode.Parse(await File.ReadAllTextAsync(path))!["credHelpers"]!["first.azurecr.io"]!
                .GetValue<string>()).IsEqualTo("acr");

            var written = File.GetLastWriteTimeUtc(path);
            File.SetLastWriteTimeUtc(path, written.AddHours(-1));
            await updater.RegisterRegistriesAsync(["first.azurecr.io"]);
            await Assert.That(File.GetLastWriteTimeUtc(path)).IsEqualTo(written.AddHours(-1));
            await Assert.That(Directory.GetFiles(dockerDirectory)).IsEquivalentTo(new[] { path });
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOCKER_CONFIG", previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Arguments("{malformed")]
    [Arguments("[]")]
    [Arguments("""{"credHelpers":null}""")]
    [Arguments("""{"credHelpers":[]}""")]
    public async Task InvalidConfigIsRejected(string original)
    {
        await Assert.That(() => DockerConfigUpdater.CreateUpdatedConfig(original, ["first.azurecr.io"]))
            .ThrowsException();
    }

    [Test]
    public async Task DiscoveryPaginatesAndDeduplicates()
    {
        var calls = 0;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            await Assert.That(request.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
            await Assert.That(request.Headers.Authorization?.Parameter).IsEqualTo("test-token");
            await Assert.That(request.RequestUri!.AbsolutePath).IsEqualTo("/providers/Microsoft.ResourceGraph/resources");
            var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            await Assert.That(payload["query"]!.GetValue<string>()).Contains("properties.loginServer");
            await Assert.That(payload["options"]!["resultFormat"]!.GetValue<string>()).IsEqualTo("objectArray");
            await Assert.That(payload["options"]!["$top"]!.GetValue<int>()).IsEqualTo(1000);
            calls++;
            if (calls == 1)
            {
                return JsonResponse("""{"data":[{"loginServer":"first.azurecr.io"}],"$skipToken":"next","resultTruncated":"true"}""");
            }

            await Assert.That(payload["options"]!["$skipToken"]!.GetValue<string>()).IsEqualTo("next");
            return JsonResponse("""{"data":[{"loginServer":"first.azurecr.io"},{"loginServer":"second.azurecr.io"}],"resultTruncated":"false"}""");
        }));

        var hosts = await new RegistryDiscovery(client, NullLoggerFactory.Instance).DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(hosts.Count).IsEqualTo(2);
        await Assert.That(hosts.Contains("second.azurecr.io")).IsTrue();
    }

    [Test]
    [Arguments("registry.privatelink.azurecr.io")]
    [Arguments("registry.example.com")]
    public async Task DiscoveryAcceptsRegistryWithoutPublicAcrSuffix(string host)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(
            new JsonObject
            {
                ["data"] = new JsonArray(new JsonObject { ["loginServer"] = host }),
                ["resultTruncated"] = "false",
            }.ToJsonString()))));

        var hosts = await new RegistryDiscovery(client, NullLoggerFactory.Instance).DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant);
        await Assert.That(hosts.Count).IsEqualTo(1);
        await Assert.That(hosts.Contains(host)).IsTrue();
    }

    [Test]
    public async Task DiscoveryReturnsNoRegistriesForEmptyResults()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(
            """{"data":[],"resultTruncated":"false","count":0,"totalRecords":0}"""))));

        var hosts = await new RegistryDiscovery(client, NullLoggerFactory.Instance).DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant);
        await Assert.That(hosts.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("""{"data":[],"resultTruncated":"true"}""")]
    [Arguments("""{}""")]
    public async Task DiscoveryRejectsIncompleteOrInvalidResults(string response)
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(response))));
        await Assert.That(async () =>
            await new RegistryDiscovery(client, NullLoggerFactory.Instance).DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DiscoverySkipsInvalidLoginServers()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(JsonResponse(
            """{"data":[{"id":"/bad","loginServer":"not a hostname"},{"id":"/missing"},{"loginServer":"good.azurecr.io"}],"resultTruncated":"false"}"""))));

        var hosts = await new RegistryDiscovery(client, NullLoggerFactory.Instance)
            .DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant);
        await Assert.That(hosts).IsEquivalentTo(new[] { "good.azurecr.io" });
    }

    [Test]
    public async Task DiscoveryReportsHttpFailure()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        await Assert.That(async () =>
            await new RegistryDiscovery(client, NullLoggerFactory.Instance).DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant))
            .Throws<RequestFailedException>();
    }

    [Test]
    [Arguments("""{"value":[]}""")]
    [Arguments("""{"value":[{"tenantId":"22222222-2222-2222-2222-222222222222"}]}""")]
    public async Task DiscoveryRejectsMissingAuthenticatedTenant(string tenants)
    {
        using var client = new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("A missing authenticated tenant must not reach Resource Graph."), tenants));

        await Assert.That(async () =>
            await new RegistryDiscovery(client, NullLoggerFactory.Instance)
                .DiscoverAsync(new AccessToken("test-token", DateTimeOffset.MaxValue), Tenant))
            .Throws<InvalidOperationException>()
            .WithMessage($"Authenticated tenant {Tenant} was not returned by Azure; discovery was not performed.");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send,
        string? tenants = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath == "/tenants"
                ? Task.FromResult(JsonResponse(tenants ??
                    """{"value":[{"id":"/tenants/22222222-2222-2222-2222-222222222222","tenantId":"22222222-2222-2222-2222-222222222222"},{"id":"/tenants/11111111-1111-1111-1111-111111111111","tenantId":"11111111-1111-1111-1111-111111111111"}]}"""))
                : send(request);
    }
}
