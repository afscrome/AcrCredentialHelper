using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper;

/// <summary>
/// Registers discovered registries in Docker's credential helper configuration.
/// </summary>
internal sealed class DockerConfigUpdater(ILoggerFactory loggerFactory) : IDockerConfigUpdater
{
    internal const string HelperName = "acr";

    private static string GetDockerConfigPath()
    {
        var directory = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
        }

        return Path.GetFullPath(Path.Combine(directory, "config.json"));
    }

    public async Task<IReadOnlyCollection<string>> RegisterRegistriesAsync(IReadOnlyCollection<string> hosts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configPath = GetDockerConfigPath();
        var logger = loggerFactory.CreateLogger<DockerConfigUpdater>();
        Logs.DockerConfigurationPath(logger, configPath);
        var original = File.Exists(configPath)
            ? await File.ReadAllTextAsync(configPath, cancellationToken)
            : null;
        var update = CreateUpdatedConfig(original, hosts, cancellationToken);
        foreach (var (registry, helper) in update.Conflicts)
        {
            Logs.CredentialHelperConflict(logger, registry, helper);
        }

        if (update.Added.Count == 0)
        {
            Logs.DockerConfigurationUnchanged(logger, configPath);
            return update.Configured;
        }

        await FileHelpers.AtomicallyWriteJson(configPath, update.Config, cancellationToken);
        Logs.RegistriesConfigured(logger, string.Join(", ", update.Added), configPath);
        return update.Configured;
    }

    /// <summary>
    /// Adds registries to <c>credHelpers</c> without replacing any existing entry.
    /// </summary>
    internal static DockerConfigUpdate CreateUpdatedConfig(string? original, IReadOnlyCollection<string> hosts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = original is null ? new JsonObject() : JsonNode.Parse(original) as JsonObject
            ?? throw new InvalidOperationException("Docker config must be a JSON object; it was not changed.");
        var helpers = GetOrCreateCredentialHelpers(config);
        var added = new List<string>();
        var configured = new List<string>();
        var conflicts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var host in hosts.Select(RegistryHelper.NormalizeHost).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!helpers.TryGetPropertyValue(host, out var existing))
            {
                helpers[host] = HelperName;
                added.Add(host);
                configured.Add(host);
            }
            else if (existing is JsonValue value && value.TryGetValue<string>(out var helper) && helper == HelperName)
            {
                configured.Add(host);
            }
            else
            {
                conflicts[host] = existing is JsonValue other && other.TryGetValue<string>(out var name)
                    ? name
                    : existing?.ToJsonString() ?? "null";
            }
        }

        return new DockerConfigUpdate(config, added, configured, conflicts);
    }

    private static JsonObject GetOrCreateCredentialHelpers(JsonObject config)
    {
        if (!config.ContainsKey("credHelpers"))
        {
            config["credHelpers"] = new JsonObject();
        }

        return config["credHelpers"] as JsonObject
            ?? throw new InvalidOperationException("Docker config 'credHelpers' must be an object; configuration was not changed.");
    }
}

/// <param name="Config">The updated Docker configuration.</param>
/// <param name="Added">Registries newly routed to this helper.</param>
/// <param name="Configured">Registries routed to this helper, whether newly added or already present.</param>
/// <param name="Conflicts">Registries left routed to another helper, mapped to that helper.</param>
internal sealed record DockerConfigUpdate(JsonObject Config, IReadOnlyList<string> Added,
    IReadOnlyList<string> Configured, IReadOnlyDictionary<string, string> Conflicts);
