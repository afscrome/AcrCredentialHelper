namespace AlexCrome.Acr.CredentialHelper;

internal static class RegistryHelper
{
    internal static async Task<string> ReadFromInput(TextReader input, CancellationToken cancellationToken = default) =>
        NormalizeHost(await input.ReadToEndAsync(cancellationToken).WaitAsync(cancellationToken));

    internal static string NormalizeHost(string registry)
    {
        var value = registry.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("A registry must be provided.", nameof(registry));
        }

        var url = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            uri.HostNameType == UriHostNameType.Unknown ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("A valid registry hostname or HTTP(S) URL must be provided.", nameof(registry));
        }

        // Docker keys credHelpers by host[:port] exactly as written, so an explicit port is significant
        // even when it matches the scheme default.
        var host = uri.Host.ToLowerInvariant();
        return HasExplicitPort(url) ? $"{host}:{uri.Port}" : host;
    }

    private static bool HasExplicitPort(string url)
    {
        var authority = url.AsSpan(url.IndexOf("://", StringComparison.Ordinal) + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if (end >= 0)
        {
            authority = authority[..end];
        }

        var colon = authority.LastIndexOf(':');
        return colon > authority.LastIndexOf(']') && colon < authority.Length - 1;
    }
}
