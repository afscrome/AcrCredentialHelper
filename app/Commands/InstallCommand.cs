using System.CommandLine;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Commands;

internal sealed class InstallCommand : Command
{
    public InstallCommand(HttpClient httpClient, ILoggerFactory loggerFactory, Option<string> tenant,
        RegistryAuthenticator authenticator, IDockerConfigUpdater configUpdater, Func<bool>? isHelperOnPath = null)
        : base("install", "Discover ACRs in a tenant and configure Docker.")
    {
        isHelperOnPath ??= HelperPathLocator.IsOnPath;
        Options.Add(tenant);
        SetAction(async (result, cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger<InstallCommand>();
            var session = authenticator.ForConfiguredTenant();
            var token = await session.GetTokenAsync(cancellationToken);

            var tenantId = session.TenantId
                ?? throw new InvalidOperationException("Authentication did not identify a tenant; discovery was not performed.");

            Logs.DiscoveringRegistries(logger, tenantId);
            var hosts = await new RegistryDiscovery(httpClient, loggerFactory).DiscoverAsync(token, tenantId, cancellationToken);
            if (hosts.Count == 0)
            {
                Logs.NoAccessibleRegistries(logger, tenantId);
                return ExitCodes.Success;
            }

            var configured = await configUpdater.RegisterRegistriesAsync(hosts, cancellationToken);
            var savedAccounts = await session.RememberForAsync(configured, cancellationToken);
            if (savedAccounts > 0)
            {
                Logs.BrokerAccountsSaved(logger, savedAccounts);
            }

            if (!isHelperOnPath())
            {
                Logs.HelperPathRequirement(logger);
            }

            return ExitCodes.Success;
        });
    }

}
