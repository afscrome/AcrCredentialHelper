using Azure.Identity;
using AlexCrome.Acr.CredentialHelper.Auth;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class TokenCredentialFactoryTests
{
    [Test]
    public async Task InteractiveBuildReturnsBrokerCredential()
    {
        var factory = new TokenCredentialFactory();
        var credential = factory.BuildInteractive();
        await Assert.That(credential is InteractiveBrowserCredential).IsTrue();
    }
}
