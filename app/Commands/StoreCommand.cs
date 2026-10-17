using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Commands;

internal sealed class StoreCommand : Command
{
    public StoreCommand(ILoggerFactory loggerFactory) : base("store", "Accept credentials without persisting them.")
    {
        SetAction((result, cancellationToken) =>
            {
                Logs.StoreIsNoOp(loggerFactory.CreateLogger<StoreCommand>());
                return Task.FromResult(ExitCodes.Success);
            });
    }

}
