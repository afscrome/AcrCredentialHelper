using System.CommandLine;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class StoreCommandTests
{
    [Test]
    public async Task ReturnsSuccessWithoutWritingOutput()
    {
        var command = new StoreCommand(NullLoggerFactory.Instance);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await command.Parse([]).InvokeAsync(new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false,
        });

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(output.ToString()).IsEqualTo(string.Empty);
        await Assert.That(error.ToString()).IsEqualTo(string.Empty);
    }
}
