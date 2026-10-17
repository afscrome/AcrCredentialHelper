namespace AlexCrome.Acr.CredentialHelper;

internal interface IDockerConfigUpdater
{
    /// <returns>The registries Docker will route to this helper.</returns>
    Task<IReadOnlyCollection<string>> RegisterRegistriesAsync(IReadOnlyCollection<string> hosts,
        CancellationToken cancellationToken = default);
}
