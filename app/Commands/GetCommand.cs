using System.CommandLine;
using System.Text.Json;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Protocol;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Commands;

internal sealed partial class GetCommand : Command
{
    public GetCommand(TextReader input, AcrCredentialResolver resolver,
        ILoggerFactory loggerFactory)
        : base("get", "Acquire credentials for an ACR registry.")
    {
        SetAction(async (result, cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger<GetCommand>();
            var serverUrl = await RegistryHelper.ReadFromInput(input, cancellationToken);

            var secret = await resolver.ResolveAsync(serverUrl, cancellationToken);
            var response = JsonSerializer.Serialize(new GetResponse(AcrCredentialResolver.AcrUsername, secret),
                DockerCredentialJsonContext.Default.GetResponse);

            await result.InvocationConfiguration.Output
                .WriteAsync(response.AsMemory(), cancellationToken);

            return ExitCodes.Success;
        });
    }
}
