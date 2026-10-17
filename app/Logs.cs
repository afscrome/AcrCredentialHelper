using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper;

internal static partial class Logs
{
    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "Silent authentication failed. Opening sign-in")]
    internal static partial void BrokerInteractionRequired(ILogger logger);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Entra authentication succeeded")]
    internal static partial void EntraAuthenticationSucceeded(ILogger logger);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Discovered {RegistryCount} accessible ACRs")]
    internal static partial void RegistriesDiscovered(ILogger logger, int registryCount);

    [LoggerMessage(EventId = 19, Level = LogLevel.Information, Message = "Discovering ACRs in tenant {TenantId}")]
    internal static partial void DiscoveringRegistries(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "Docker configuration path: {ConfigPath}")]
    internal static partial void DockerConfigurationPath(ILogger logger, string configPath);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "Saved broker account metadata for {RegistryCount} registries")]
    internal static partial void BrokerAccountsSaved(ILogger logger, int registryCount);

    [LoggerMessage(EventId = 22, Level = LogLevel.Information, Message = "Configured registries {Registries} in {ConfigPath}")]
    internal static partial void RegistriesConfigured(ILogger logger, string registries, string configPath);

    [LoggerMessage(EventId = 28, Level = LogLevel.Information, Message = "Store is a no-op; credentials are not persisted.")]
    internal static partial void StoreIsNoOp(ILogger logger);

    [LoggerMessage(EventId = 29, Level = LogLevel.Information, Message = "Removed saved authentication record for registry {Registry}")]
    internal static partial void Erase(ILogger logger, string registry);

    [LoggerMessage(EventId = 30, Level = LogLevel.Error, Message = "Command {Command} failed")]
    internal static partial void CommandFailed(ILogger logger, Exception exception, string command);

    [LoggerMessage(EventId = 31, Level = LogLevel.Information, Message = "Command {Command} cancelled")]
    internal static partial void CommandCancelled(ILogger logger, string command);

    [LoggerMessage(EventId = 32, Level = LogLevel.Warning, Message = "No accessible ACRs found in tenant {TenantId}; Docker configuration was not changed")]
    internal static partial void NoAccessibleRegistries(ILogger logger, string tenantId);

    [LoggerMessage(EventId = 33, Level = LogLevel.Warning, Message = "\"docker-credential-acr\" was not found on PATH.  Docker may fail to use the Credential Helper.")]
    internal static partial void HelperPathRequirement(ILogger logger);

    [LoggerMessage(EventId = 34, Level = LogLevel.Warning, Message = "Authentication cancelled")]
    internal static partial void AuthenticationCancelled(ILogger logger);

    [LoggerMessage(EventId = 35, Level = LogLevel.Information, Message = "Using existing account {Username} to authenticate with registry {Registry}")]
    internal static partial void ExistingAccountIdentified(ILogger logger, string username, string registry);

    [LoggerMessage(EventId = 36, Level = LogLevel.Warning, Message = "Authentication did not complete within {Timeout}")]
    internal static partial void AuthenticationTimedOut(ILogger logger, TimeSpan timeout);

    [LoggerMessage(EventId = 37, Level = LogLevel.Warning, Message = "Skipping registry {ResourceId}: Resource Graph returned invalid login server '{LoginServer}'")]
    internal static partial void InvalidLoginServerSkipped(ILogger logger, string resourceId, string loginServer);

    [LoggerMessage(EventId = 38, Level = LogLevel.Warning, Message = "Registry {Registry} is already configured to use credential helper '{Helper}'; it was not changed")]
    internal static partial void CredentialHelperConflict(ILogger logger, string registry, string helper);

    [LoggerMessage(EventId = 39, Level = LogLevel.Information, Message = "All discovered registries are already configured in {ConfigPath}; it was not changed")]
    internal static partial void DockerConfigurationUnchanged(ILogger logger, string configPath);
}
