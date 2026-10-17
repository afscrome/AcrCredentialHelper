using Microsoft.Extensions.Configuration;

namespace AlexCrome.Acr.CredentialHelper.Tests;

internal static class TestConfiguration
{
    internal static IConfiguration Empty { get; } = new ConfigurationBuilder().Build();
}
