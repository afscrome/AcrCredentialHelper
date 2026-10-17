using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper;

internal static class HelperConfiguration
{
    internal static ConfigurationManager Build(IEnumerable<KeyValuePair<string, string?>>? overrides = null)
    {
        var configuration = new ConfigurationManager();
        configuration
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Warning",
                // The Azure SDK convention is honored, but helper-specific settings take precedence.
                [TenantHelper.TenantIdKey] = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
            })
            .AddEnvironmentVariables("ACR_CREDENTIAL_HELPER_")
            .AddInMemoryCollection(overrides ?? []);
        return configuration;
    }

    internal static ILoggerFactory CreateLoggerFactory(IConfiguration configuration) =>
        LoggerFactory.Create(builder => builder
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace)
            .AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            }));
}
