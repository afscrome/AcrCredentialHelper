using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Azure.Core;
using Azure.Identity;
using Azure.Identity.Broker;

namespace AlexCrome.Acr.CredentialHelper.Auth;

public sealed class TokenCredentialFactory
{
    public TokenCredential BuildInteractive(string? tenantId = null, AuthenticationRecord? record = null)
    {
        var parentWindow = OperatingSystem.IsWindows()
            ? BrokerWindow.GetConsoleOrTerminalWindow()
            : IntPtr.Zero;

        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialBrokerOptions(parentWindow)
        {
            TenantId = tenantId ?? record?.TenantId,
            UseDefaultBrokerAccount = tenantId is not null || record is not null,
            AuthenticationRecord = record,
            DisableAutomaticAuthentication = true,
        });
    }
}

[SupportedOSPlatform("windows")]
internal static partial class BrokerWindow
{
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    public static IntPtr GetConsoleOrTerminalWindow() => GetAncestor(GetConsoleWindow(), 3);
}
