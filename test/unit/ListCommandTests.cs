using System.CommandLine;
using System.Text.Json.Nodes;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;
using static TUnit.Mocks.Arguments.Arg;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class ListCommandTests
{
    [Test]
    public async Task EmptyStoreWritesEmptyMapToEachInvocationsOutput()
    {
        var command = new ListCommand(new RegistryAuthenticator(
            new InMemoryRegistryAccountMappingStore(), TestConfiguration.Empty, NullLoggerFactory.Instance));
        using var first = new StringWriter();
        using var second = new StringWriter();
        using var error = new StringWriter();

        foreach (var output in new[] { first, second })
        {
            var exit = await command.Parse([]).InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                Error = error,
                EnableDefaultExceptionHandler = false,
            });
            await Assert.That(exit).IsEqualTo(0);
            await Assert.That(output.ToString()).IsEqualTo("{}");
        }

        await Assert.That(error.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ListsSavedRegistriesWithDockerUsernameWithoutAuthenticating()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var record = await StubBrokerCredential.ReadRecordAsync(username: "private-account@example.com");
        await store.Update("https://FIRST.azurecr.io/v2/", record);
        await store.Update("second.example.com", record);
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance,
            (_, _) => throw new InvalidOperationException("List must not construct a credential."));
        var command = new ListCommand(authenticator);
        using var output = new StringWriter();

        var exit = await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            EnableDefaultExceptionHandler = false,
        });

        await Assert.That(exit).IsEqualTo(0);
        var listed = JsonNode.Parse(output.ToString())!.AsObject();
        await Assert.That(listed.Count).IsEqualTo(2);
        await Assert.That(listed["first.azurecr.io"]!.GetValue<string>()).IsEqualTo(AcrCredentialResolver.AcrUsername);
        await Assert.That(listed["second.example.com"]!.GetValue<string>()).IsEqualTo(AcrCredentialResolver.AcrUsername);
        await Assert.That(output.ToString()).DoesNotContain("private-account");
        await Assert.That(output.ToString()).DoesNotContain("user.tenant");
        await Assert.That(store.Records.Keys).IsEquivalentTo(new[] { "first.azurecr.io", "second.example.com" });
    }

    [Test]
    public async Task PropagatesStoreFailureWithoutWritingOutput()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        store.List(Any<CancellationToken>())
            .Throws(new InvalidOperationException("Cannot read saved accounts."));
        var command = new ListCommand(new RegistryAuthenticator(store.Object, TestConfiguration.Empty, NullLoggerFactory.Instance));
        using var output = new StringWriter();

        await Assert.That(async () => await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            EnableDefaultExceptionHandler = false,
        })).Throws<InvalidOperationException>().WithMessage("Cannot read saved accounts.");
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task HonorsCancellation()
    {
        var command = new ListCommand(new RegistryAuthenticator(
            new InMemoryRegistryAccountMappingStore(), TestConfiguration.Empty, NullLoggerFactory.Instance));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();

        await Assert.That(async () => await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            EnableDefaultExceptionHandler = false,
        }, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }
}
