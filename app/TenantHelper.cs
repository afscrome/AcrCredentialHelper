using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace AlexCrome.Acr.CredentialHelper;

internal static class TenantHelper
{
    internal const string TenantIdKey = "TenantId";

    internal static string? GetTenant(IConfiguration configuration) =>
        configuration[TenantIdKey] is { } tenant && !string.IsNullOrWhiteSpace(tenant) ? tenant.Trim() : null;
}

internal static class AuthenticationTimeout
{
    internal const string Key = "AuthenticationTimeout";
    internal static readonly TimeSpan Default = TimeSpan.FromMinutes(5);

    internal static TimeSpan Get(IConfiguration configuration)
    {
        var value = configuration[Key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeout) && timeout > TimeSpan.Zero)
        {
            return timeout;
        }

        throw new InvalidOperationException(
            $"Configuration value '{Key}' must be a positive time span such as '00:05:00'; it was '{value}'.");
    }
}
