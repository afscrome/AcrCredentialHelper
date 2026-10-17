using AlexCrome.Acr.CredentialHelper.Auth;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public static class TestSetup
{
    // Tests run in parallel, so the machine-wide sign-in lock would make them contend with each other.
    [Before(TestDiscovery)]
    public static void DisableGlobalInteractiveLock() =>
        InteractiveAuthenticationLock.Default = InteractiveAuthenticationLock.None;
}
