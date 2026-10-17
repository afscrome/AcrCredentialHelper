using AlexCrome.Acr.CredentialHelper;
using Microsoft.Extensions.Azure;

try
{
    using var configuration = HelperConfiguration.Build();
    using var loggerFactory = HelperConfiguration.CreateLoggerFactory(configuration);
    using var azureLogForwarder = new AzureEventSourceLogForwarder(new AzureSdkLoggerFactory(loggerFactory));
    azureLogForwarder.Start();
    using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    return await new CredentialHelperApp(Console.In, Console.Out, Console.Error, httpClient,
        configuration, loggerFactory).RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unable to start credential helper: {ex.Message}");
    return ExitCodes.Failure;
}
