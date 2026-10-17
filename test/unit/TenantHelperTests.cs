using Azure.Core;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class TenantHelperTests
{
    [Test]
    [Arguments("configured-tenant", "configured-tenant")]
    [Arguments(" configured-tenant ", "configured-tenant")]
    [Arguments("", null)]
    [Arguments("  ", null)]
    [Arguments(null, null)]
    public async Task TenantIsReadFromConfiguration(string? value, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TenantId"] = value })
            .Build();
        await Assert.That(TenantHelper.GetTenant(configuration)).IsEqualTo(expected);
    }

    [Test]
    [NotInParallel("AZURE_TENANT_ID")]
    public async Task HelperTenantSettingOverridesAzureTenantId()
    {
        var previousAzure = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");
        var previousHelper = Environment.GetEnvironmentVariable("ACR_CREDENTIAL_HELPER_TenantId");
        try
        {
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "azure-tenant");
            Environment.SetEnvironmentVariable("ACR_CREDENTIAL_HELPER_TenantId", null);
            await Assert.That(TenantHelper.GetTenant(HelperConfiguration.Build())).IsEqualTo("azure-tenant");

            Environment.SetEnvironmentVariable("ACR_CREDENTIAL_HELPER_TenantId", "helper-tenant");
            await Assert.That(TenantHelper.GetTenant(HelperConfiguration.Build())).IsEqualTo("helper-tenant");
            await Assert.That(TenantHelper.GetTenant(HelperConfiguration.Build([new("TenantId", "override")])))
                .IsEqualTo("override");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", previousAzure);
            Environment.SetEnvironmentVariable("ACR_CREDENTIAL_HELPER_TenantId", previousHelper);
        }
    }

    [Test]
    public async Task RegistrySessionUsesConfiguredTenant()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TenantId"] = "configured-tenant" })
            .Build();
        var authenticator = new RegistryAuthenticator(new InMemoryRegistryAccountMappingStore(), configuration,
            NullLoggerFactory.Instance);

        await Assert.That((await authenticator.ForRegistryAsync("registry.example.com")).TenantId)
            .IsEqualTo("configured-tenant");
        await Assert.That(authenticator.ForConfiguredTenant().TenantId).IsEqualTo("configured-tenant");
    }

    [Test]
    [Arguments(null, 300)]
    [Arguments("00:00:30", 30)]
    public async Task AuthenticationTimeoutIsReadFromConfiguration(string? value, int expectedSeconds)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthenticationTimeout"] = value })
            .Build();
        await Assert.That(AuthenticationTimeout.Get(configuration)).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    [Arguments("soon")]
    [Arguments("00:00:00")]
    [Arguments("-00:00:01")]
    public async Task InvalidAuthenticationTimeoutIsRejected(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthenticationTimeout"] = value })
            .Build();
        await Assert.That(() => AuthenticationTimeout.Get(configuration)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AuthenticationThatNeverCompletesTimesOut()
    {
        var session = new AuthenticationSession(new NeverCompletingCredential(), "tenant",
            new InMemoryRegistryAccountMappingStore(), NullLogger.Instance, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.That(async () => await session.GetTokenAsync()).Throws<TimeoutException>();
        await Assert.That(async () => await session.RememberForAsync(["registry.example.com"]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        var session = new AuthenticationSession(new NeverCompletingCredential(), "tenant",
            new InMemoryRegistryAccountMappingStore(), NullLogger.Instance, timeout: TimeSpan.FromMinutes(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.That(async () => await session.GetTokenAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task HelperPathLocatorFindsExecutableOnPath()
    {
        var directory = Directory.CreateTempSubdirectory("acr-path-").FullName;
        try
        {
            var name = OperatingSystem.IsWindows() ? "docker-credential-acr.EXE" : "docker-credential-acr";
            var extensions = OperatingSystem.IsWindows() ? ".COM;.EXE" : null;
            var path = string.Join(Path.PathSeparator, Path.Combine(directory, "missing"), $"\"{directory}\"");
            await Assert.That(HelperPathLocator.IsOnPath(path, extensions)).IsFalse();

            var file = Path.Combine(directory, name);
            await File.WriteAllTextAsync(file, string.Empty);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await Assert.That(HelperPathLocator.IsOnPath(path, extensions)).IsFalse();
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            await Assert.That(HelperPathLocator.IsOnPath(path, extensions)).IsTrue();
            await Assert.That(HelperPathLocator.IsOnPath(null, extensions)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class NeverCompletingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        // Ignores cancellation, like a broker prompt that the user never answers.
        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, CancellationToken.None);
            return default;
        }
    }
}
