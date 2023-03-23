using Xunit.Abstractions;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests.Util;

internal class DisconnectableTestOutputLogger : ITestOutputHelper
{
    private ITestOutputHelper? _delegateLogger;

    public DisconnectableTestOutputLogger(ITestOutputHelper delegateLogger)
    {
        _delegateLogger = delegateLogger;
    }

    public void Disconnect()
    {
        _delegateLogger = null;
    }

    public void WriteLine(string message) => _delegateLogger?.WriteLine(message);

    public void WriteLine(string format, params object[] args) => _delegateLogger?.WriteLine(format, args);
}