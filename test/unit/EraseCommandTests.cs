using System.CommandLine;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Mocks;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class EraseCommandTests
{
    [Test]
    public async Task RemovesOnlyRequestedRegistryAndIsIdempotent()
    {
        var store = new InMemoryRegistryAccountMappingStore();
        var record = await StubBrokerCredential.ReadRecordAsync();
        await store.Update("first.azurecr.io", record);
        await store.Update("second.azurecr.io", record);
        var authenticator = new RegistryAuthenticator(store, TestConfiguration.Empty, NullLoggerFactory.Instance);
        using var output = new StringWriter();

        for (var i = 0; i < 2; i++)
        {
            using var input = new StringReader("https://FIRST.azurecr.io\n");
            var command = new EraseCommand(input, authenticator, NullLoggerFactory.Instance);
            var exit = await command.Parse([]).InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                EnableDefaultExceptionHandler = false,
            });
            await Assert.That(exit).IsEqualTo(0);
        }

        await Assert.That(store.Records.Keys).IsEquivalentTo(new[] { "second.azurecr.io" });
        await Assert.That(store.Records["second.azurecr.io"]).IsSameReferenceAs(record);
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task RejectsEmptyInputWithoutRemovingAccounts()
    {
        var store = Mock.Of<IRegistryAccountMappingStore>(MockBehavior.Strict);
        var authenticator = new RegistryAuthenticator(store.Object, TestConfiguration.Empty, NullLoggerFactory.Instance);
        using var input = new StringReader("   ");
        var command = new EraseCommand(input, authenticator, NullLoggerFactory.Instance);

        await Assert.That(async () => await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
        })).Throws<ArgumentException>();
    }
}
