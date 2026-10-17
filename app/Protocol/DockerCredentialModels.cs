using System.Text.Json.Serialization;

namespace AlexCrome.Acr.CredentialHelper.Protocol;

/// <summary>
/// Payload sent by Docker on stdin for the "store" command.
/// See: https://github.com/docker/docker-credential-helpers#development
/// </summary>
public sealed class StoreRequest
{
    public string? ServerURL { get; set; }
    public string? Username { get; set; }
    public string? Secret { get; set; }
}

/// <summary>
/// Payload returned on stdout for a successful "get" command.
/// </summary>
public sealed class GetResponse
{
    public GetResponse(string username, string secret)
    {
        Username = username;
        Secret = secret;
    }

    public string Username { get; set; }
    public string Secret { get; set; }
}

[JsonSerializable(typeof(StoreRequest))]
[JsonSerializable(typeof(GetResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class DockerCredentialJsonContext : JsonSerializerContext
{
}
