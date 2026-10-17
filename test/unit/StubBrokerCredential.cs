using System.Text;
using Azure.Core;
using Azure.Identity;

namespace AlexCrome.Acr.CredentialHelper.Tests;

internal sealed class StubBrokerCredential(AuthenticationRecord? record = null)
    : InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { AuthenticationRecord = record })
{
    internal const string RecordJson = """
        {"username":"user@example.com","authority":"login.microsoftonline.com","homeAccountId":"user.tenant","tenantId":"tenant","clientId":"client","version":"1.0"}
        """;

    internal bool RequiresInteraction { get; set; }
    internal int InteractiveCalls { get; private set; }
    internal List<TokenRequestContext> Contexts { get; } = [];

    internal static async Task<AuthenticationRecord> ReadRecordAsync(
        string username = "user@example.com", string tenantId = "tenant")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            RecordJson.Replace("user@example.com", username)
                .Replace("\"tenantId\":\"tenant\"", $"\"tenantId\":\"{tenantId}\"")));
        return await AuthenticationRecord.DeserializeAsync(stream);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Contexts.Add(requestContext);
        if (RequiresInteraction)
        {
            throw new AuthenticationRequiredException("Sign in required", requestContext);
        }

        return ValueTask.FromResult(new AccessToken("secret-access-token", DateTimeOffset.UtcNow.AddHours(1)));
    }

    public override async Task<AuthenticationRecord> AuthenticateAsync(TokenRequestContext requestContext,
        CancellationToken cancellationToken = default)
    {
        InteractiveCalls++;
        RequiresInteraction = false;
        if (record is not null)
        {
            return record;
        }

        var json = requestContext.TenantId is { } tenant
            ? RecordJson.Replace("\"tenantId\":\"tenant\"", $"\"tenantId\":\"{tenant}\"")
            : RecordJson;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
    }
}
