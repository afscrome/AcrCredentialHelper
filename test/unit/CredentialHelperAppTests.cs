using Azure.Core;
using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using TUnit.Mocks;
using static TUnit.Mocks.Arguments.Arg;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class CredentialHelperAppTests
{
    [Test]
    [Arguments("get", true)]
    [Arguments("get", false)]
    [Arguments("install", true)]
    [Arguments("install", false)]
    public async Task BrokerCancellationReturnsCancelledButAuthenticationFailureReturnsFailure(string command, bool cancelled)
    {
        const string tenant = "11111111-1111-1111-1111-111111111111";
        var credential = Mock.Of<InteractiveBrowserCredential>(MockBehavior.Strict);
        credential.GetTokenAsync(Any<TokenRequestContext>(), Any<CancellationToken>())
            .Throws(new AuthenticationFailedException("Authentication failed.",
                new MsalClientException(cancelled
                    ? MsalError.AuthenticationCanceledError
                    : MsalError.AuthenticationFailed)));
        var store = new InMemoryRegistryAccountMappingStore();
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => credential.Object);
        using var configuration = new ConfigurationManager
        {
            ["TenantId"] = tenant,
        };
        using var client = new HttpClient();
        using var input = new StringReader("registry.example.com");
        using var output = new StringWriter();
        var app = new CredentialHelperApp(input, output, TextWriter.Null, client, configuration,
            NullLoggerFactory.Instance, authenticator);

        var exit = await app.RunAsync([command]);

        await Assert.That(exit).IsEqualTo(cancelled ? 130 : 1);
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
        await Assert.That(store.Records).IsEmpty();
    }

    [Test]
    public async Task CommandLineLogLevelOverridesConfiguration()
    {
        using var configuration = new ConfigurationManager
        {
            ["Logging:LogLevel:Default"] = "Warning",
        };
        using var client = new HttpClient();
        var authenticator = new RegistryAuthenticator(new InMemoryRegistryAccountMappingStore(), TestConfiguration.Empty, NullLoggerFactory.Instance);
        var app = new CredentialHelperApp(TextReader.Null, TextWriter.Null, TextWriter.Null, client,
            configuration, NullLoggerFactory.Instance, authenticator);

        var exit = await app.RunAsync(["list", "--log-level", "None"]);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(configuration["Logging:LogLevel:Default"]).IsEqualTo("None");
    }

    [Test]
    public async Task CommandFailureReturnsFailureWithoutProtocolOutput()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        store.List(Any<CancellationToken>())
            .Throws(new InvalidOperationException("Cannot read saved accounts."));
        var authenticator = new RegistryAuthenticator(store.Object, TestConfiguration.Empty, NullLoggerFactory.Instance);
        using var configuration = new ConfigurationManager();
        using var client = new HttpClient();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var app = new CredentialHelperApp(TextReader.Null, output, error, client, configuration,
            NullLoggerFactory.Instance, authenticator);

        var exit = await app.RunAsync(["list"]);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
        await Assert.That(error.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("get")]
    [Arguments("erase")]
    public async Task CancellationInterruptsPendingInput(string command)
    {
        using var input = new PendingInput();
        using var output = new StringWriter();
        using var client = new HttpClient();
        using var configuration = new ConfigurationManager();
        var authenticator = new RegistryAuthenticator(new InMemoryRegistryAccountMappingStore(), TestConfiguration.Empty, NullLoggerFactory.Instance);
        using var cancellation = new CancellationTokenSource();
        var run = new CredentialHelperApp(input, output, TextWriter.Null, client, configuration,
            NullLoggerFactory.Instance, authenticator).RunAsync([command], cancellation.Token);
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.That(await run.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(130);
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }

    private sealed class PendingInput : TextReader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _input = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            return _input.Task;
        }

        protected override void Dispose(bool disposing)
        {
            _input.TrySetResult(string.Empty);
            base.Dispose(disposing);
        }
    }
}
