using System.CommandLine;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Commands;

internal sealed class EraseCommand : Command
{
    public EraseCommand(TextReader input, RegistryAuthenticator authenticator,
        ILoggerFactory loggerFactory) : base("erase", "Remove the saved account for a registry.")
    {
        SetAction(async (result, cancellationToken) =>
            {
                var registry = await RegistryHelper.ReadFromInput(input, cancellationToken);

                await authenticator.ForgetAsync(registry, cancellationToken);

                Logs.Erase(loggerFactory.CreateLogger<EraseCommand>(), registry);

                return ExitCodes.Success;
            });
    }

}
