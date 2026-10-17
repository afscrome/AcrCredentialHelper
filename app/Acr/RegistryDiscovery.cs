using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Acr;

internal sealed class RegistryDiscovery(HttpClient httpClient, ILoggerFactory loggerFactory)
{
    private readonly ILogger<RegistryDiscovery> _logger = loggerFactory.CreateLogger<RegistryDiscovery>();

    internal async Task<IReadOnlyCollection<string>> DiscoverAsync(AccessToken token, string tenantId,
        CancellationToken cancellationToken = default)
    {
        var client = new ArmClient(DelegatedTokenCredential.Create((_, _) => token), defaultSubscriptionId: null,
            new ArmClientOptions { Transport = new HttpClientTransport(httpClient) });
        var tenant = await client.GetTenants().GetAllAsync(cancellationToken)
            .FirstOrDefaultAsync(candidate =>
                string.Equals(candidate.Data.TenantId?.ToString(), tenantId, StringComparison.OrdinalIgnoreCase),
                cancellationToken)
            ?? throw new InvalidOperationException($"Authenticated tenant {tenantId} was not returned by Azure; discovery was not performed.");
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? skipToken = null;
        do
        {
            var query = CreateRegistryQuery(skipToken);
            var response = await tenant.GetResourcesAsync(query, cancellationToken);

            var pageHosts = ReadRegistryHosts(response.Value);
            hosts.UnionWith(pageHosts);

            skipToken = response.Value.SkipToken;
            if (string.IsNullOrEmpty(skipToken))
            {
                skipToken = null;
                if (response.Value.ResultTruncated == ResultTruncated.True)
                {
                    throw new InvalidOperationException("Resource Graph truncated discovery without a continuation token; configuration was not changed.");
                }
            }
        }
        while (skipToken is not null);

        Logs.RegistriesDiscovered(_logger, hosts.Count);
        return hosts;
    }

    private static ResourceQueryContent CreateRegistryQuery(string? skipToken) =>
        // Keeping id in the projection allows Resource Graph to paginate results.
        new("""
            Resources
                | where type =~ 'microsoft.containerregistry/registries'
                | project id, loginServer = tostring(properties.loginServer)
                | order by id asc
            """)
        {
            Options = new ResourceQueryRequestOptions
            {
                ResultFormat = ResultFormat.ObjectArray,
                Top = 1000,
                SkipToken = skipToken,
            },
        };

    private IReadOnlyCollection<string> ReadRegistryHosts(ResourceQueryResult result)
    {
        var rows = (result.Data is null ? null : JsonNode.Parse(result.Data.ToMemory().Span) as JsonArray)
            ?? throw new InvalidOperationException("Resource Graph response is missing its resource array.");
        var hosts = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            string? host = null;
            if (row?["loginServer"] is JsonValue value && value.TryGetValue<string>(out var loginServer))
            {
                host = loginServer;
            }

            if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) != UriHostNameType.Dns)
            {
                var resourceId = row?["id"] is JsonValue id && id.TryGetValue<string>(out var text) ? text : "(unknown)";
                Logs.InvalidLoginServerSkipped(_logger, resourceId, host ?? "(missing)");
                continue;
            }

            hosts.Add(RegistryHelper.NormalizeHost(host));
        }

        return hosts;
    }
}
