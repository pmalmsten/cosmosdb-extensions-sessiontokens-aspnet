using Divergic.Logging.Xunit;
using Xunit.Abstractions;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests.Util;

internal class IntegrationTestLoggerProvider : ILoggerProvider
{
    private readonly ILoggerFactory _logFactory;

    public IntegrationTestLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _logFactory = LogFactory.Create(testOutputHelper);
    }

    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SynchronizedLogger(_logFactory, _logFactory.CreateLogger(categoryName));
    }
}