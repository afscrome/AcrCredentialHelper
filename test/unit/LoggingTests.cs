using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper.Tests;

public class LoggingTests
{
    [Test]
    [Arguments("Azure.Identity", LogLevel.Information, LogLevel.Debug)]
    [Arguments("Azure.Identity", LogLevel.Warning, LogLevel.Debug)]
    [Arguments("Azure.Identity", LogLevel.Error, LogLevel.Debug)]
    [Arguments("Azure.Identity", LogLevel.Critical, LogLevel.Debug)]
    [Arguments("Azure.Identity", LogLevel.Debug, LogLevel.Debug)]
    [Arguments("Azure.Identity", LogLevel.Trace, LogLevel.Trace)]
    [Arguments("Azure.Identity.Broker", LogLevel.Information, LogLevel.Debug)]
    [Arguments("Azure.Core", LogLevel.Information, LogLevel.Debug)]
    [Arguments("Azure.ResourceManager", LogLevel.Warning, LogLevel.Debug)]
    public async Task AzureSdkEventsAreCappedAtDebug(string category, LogLevel original, LogLevel expected)
    {
        using var provider = new LevelCaptureProvider();
        using var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(provider));
        using var azureFactory = new AzureSdkLoggerFactory(factory);

        azureFactory.CreateLogger(category).Log(original, "SDK event");

        await Assert.That(provider.Levels.Single()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Azure.Identity", LogLevel.Information, false)]
    [Arguments("Azure.Identity", LogLevel.Debug, true)]
    [Arguments("Azure.Core", LogLevel.Information, false)]
    [Arguments("Azure.Core", LogLevel.Debug, true)]
    public async Task AzureSdkUsesCappedLevelForFiltering(string category, LogLevel minimum, bool expected)
    {
        using var provider = new LevelCaptureProvider();
        using var factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(minimum)
            .AddProvider(provider));
        using var azureFactory = new AzureSdkLoggerFactory(factory);
        var logger = azureFactory.CreateLogger(category);

        await Assert.That(logger.IsEnabled(LogLevel.Error)).IsEqualTo(expected);
        logger.LogError("SDK event");
        await Assert.That(provider.Levels.Count).IsEqualTo(expected ? 1 : 0);
        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();
        logger.Log(LogLevel.None, "SDK event");
        await Assert.That(provider.Levels.Count).IsEqualTo(expected ? 1 : 0);
    }

    [Test]
    public async Task LoggingDefaultsToWarning()
    {
        // An explicit category avoids relying on a developer's Default environment setting.
        using var configuration = HelperConfiguration.Build(new Dictionary<string, string?> { ["Logging:LogLevel:TestCategory"] = "Warning" });
        using var factory = HelperConfiguration.CreateLoggerFactory(configuration);
        var logger = factory.CreateLogger("TestCategory");
        await Assert.That(logger.IsEnabled(LogLevel.Information)).IsFalse();
        await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
    }

    [Test]
    public async Task CommandLineConfiguresLoggingAndInstallTenant()
    {
        using var configuration = HelperConfiguration.Build(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Debug",
            ["TenantId"] = "test-tenant",
        });
        using var factory = HelperConfiguration.CreateLoggerFactory(configuration);
        var logger = factory.CreateLogger("CommandLineTest");
        await Assert.That(configuration["Logging:LogLevel:Default"]).IsEqualTo("Debug");
        await Assert.That(configuration["TenantId"]).IsEqualTo("test-tenant");
        await Assert.That(logger.IsEnabled(LogLevel.Debug)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.Trace)).IsFalse();
    }

    [Test]
    public async Task StandardConfigurationKeysSupportCategoryOverrides()
    {
        using var configuration = HelperConfiguration.Build(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "None",
            ["Logging:LogLevel:DiagnosticCategory"] = "Trace",
        });
        using var factory = HelperConfiguration.CreateLoggerFactory(configuration);
        await Assert.That(factory.CreateLogger("DiagnosticCategory").IsEnabled(LogLevel.Trace)).IsTrue();
        await Assert.That(factory.CreateLogger("OtherCategory").IsEnabled(LogLevel.Critical)).IsFalse();
    }

    private sealed class LevelCaptureProvider : ILoggerProvider, ILogger
    {
        internal List<LogLevel> Levels { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Dispose() { }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
