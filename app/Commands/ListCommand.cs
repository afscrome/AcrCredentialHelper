using System.CommandLine;
using System.Text.Json;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Protocol;

namespace AlexCrome.Acr.CredentialHelper.Commands;

internal sealed class ListCommand : Command
{
    public ListCommand(RegistryAuthenticator authenticator) : base("list", "List registries with saved accounts.")
    {
        SetAction(async (result, cancellationToken) =>
            {
                var registries = await authenticator.ListRegistriesAsync(cancellationToken);

                var credentials = registries.ToDictionary(registry => registry, _ => AcrCredentialResolver.AcrUsername);
                var response = JsonSerializer.Serialize(credentials, DockerCredentialJsonContext.Default.DictionaryStringString);

                await result.InvocationConfiguration.Output
                    .WriteAsync(response.AsMemory(), cancellationToken);

                return ExitCodes.Success;
            });
    }
}
