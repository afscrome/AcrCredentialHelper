using Microsoft.Extensions.Logging;

namespace AlexCrome.Acr.CredentialHelper;

internal sealed class AzureSdkLoggerFactory(ILoggerFactory loggerFactory) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) =>
        new SdkLogger(loggerFactory.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => loggerFactory.AddProvider(provider);

    // The caller owns the underlying factory.
    public void Dispose() { }

    private sealed class SdkLogger(ILogger logger) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && logger.IsEnabled(CapLevel(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.None)
            {
                logger.Log(CapLevel(logLevel), eventId, state, exception, formatter);
            }
        }

        private static LogLevel CapLevel(LogLevel level) =>
            level > LogLevel.Debug ? LogLevel.Debug : level;
    }
}
