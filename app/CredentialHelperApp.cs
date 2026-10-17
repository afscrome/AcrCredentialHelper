using System.CommandLine;
using System.Security.Cryptography;
using AlexCrome.Acr.CredentialHelper.Acr;
using AlexCrome.Acr.CredentialHelper.Auth;
using AlexCrome.Acr.CredentialHelper.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper;

public sealed class CredentialHelperApp(TextReader stdin, TextWriter stdout, TextWriter stderr, HttpClient httpClient,
    IConfigurationRoot configuration, ILoggerFactory loggerFactory, RegistryAccountMappingStore? recordStore = null)
{
    private readonly RegistryAuthenticator? _authenticator;

    internal CredentialHelperApp(TextReader stdin, TextWriter stdout, TextWriter stderr, HttpClient httpClient,
        IConfigurationRoot configuration, ILoggerFactory loggerFactory, RegistryAuthenticator authenticator)
        : this(stdin, stdout, stderr, httpClient, configuration, loggerFactory)
    {
        _authenticator = authenticator;
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {

            cancellationToken.ThrowIfCancellationRequested();
            var root = new RootCommand("Acquire Docker credentials for Azure Container Registry.");
            var level = new Option<LogLevel?>("--log-level", "--Logging:LogLevel:Default")
            {
                Description = "Logging verbosity (default: Warning). Logs are written to stderr.",
                Recursive = true,
            };
            root.Options.Add(level);
            var tenant = new Option<string>("--tenant", "--TenantId")
            {
                Description = "Optional Entra tenant ID or domain (defaults to configuration, AZURE_TENANT_ID, or the authenticated account's tenant).",
            };
            var registryAuthenticator = _authenticator
                ?? new RegistryAuthenticator(recordStore ?? new RegistryAccountMappingStore(), configuration, loggerFactory);
            var resolver = new AcrCredentialResolver(registryAuthenticator, httpClient);
            root.Subcommands.Add(new InstallCommand(httpClient, loggerFactory, tenant, registryAuthenticator,
                new DockerConfigUpdater(loggerFactory)));
            root.Subcommands.Add(new GetCommand(stdin, resolver, loggerFactory));
            root.Subcommands.Add(new StoreCommand(loggerFactory));
            root.Subcommands.Add(new EraseCommand(stdin, registryAuthenticator, loggerFactory));
            root.Subcommands.Add(new ListCommand(registryAuthenticator));

            var result = root.Parse(args);
            try
            {
                if (result.Errors.Count == 0)
                {
                    if (result.GetValue(level) is { } logLevel)
                    {
                        configuration["Logging:LogLevel:Default"] = logLevel.ToString();
                    }

                    if (result.CommandResult.Command.Name == "install" && result.GetValue(tenant) is { } tenantId)
                    {
                        configuration["TenantId"] = tenantId;
                    }

                    configuration.Reload();
                }

                return await result.InvokeAsync(new InvocationConfiguration
                {
                    Output = stdout,
                    Error = stderr,
                    EnableDefaultExceptionHandler = false,
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Logs.CommandCancelled(loggerFactory.CreateLogger<CredentialHelperApp>(), result.CommandResult.Command.Name);
                return ExitCodes.Cancelled;
            }
            catch (Exception ex)
            {
                Logs.CommandFailed(loggerFactory.CreateLogger<CredentialHelperApp>(), ex, result.CommandResult.Command.Name);
                return ExitCodes.Failure;
            }
        }
        catch (OperationCanceledException)
        {
            Logs.CommandCancelled(loggerFactory.CreateLogger<CredentialHelperApp>(), args.FirstOrDefault() ?? "(none)");
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Unable to start credential helper: {ex.Message}");
            return ExitCodes.Failure;
        }
    }

}
