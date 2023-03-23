namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests.Util;

internal class SynchronizedLogger : ILogger
{
    private readonly object _globalLogLock;
    private readonly ILogger _delegate;

    public SynchronizedLogger(object globalLogLock, ILogger @delegate)
    {
        _globalLogLock = globalLogLock;
        _delegate = @delegate;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_globalLogLock)
        {
            _delegate.Log(logLevel, eventId, state, exception, formatter);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        lock (_globalLogLock)
        {
            return _delegate.IsEnabled(logLevel);
        }
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        lock (_globalLogLock)
        {
            return _delegate.BeginScope(state);
        }
    }
}