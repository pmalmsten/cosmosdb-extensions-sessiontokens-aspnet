using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore.UnitTests.Interceptors;

public class CosmosDbClientInterceptorTests
{
    private readonly CosmosClient _fakeCosmosClient = A.Fake<CosmosClient>();
    private readonly ProxyGenerator _proxyGenerator = new();
    private readonly IProxyGenerator _fakeProxyGenerator = A.Fake<IProxyGenerator>();
    private readonly GetCurrentContextDelegate<string> _fakeGetCurrentContextDelegate = A.Fake<GetCurrentContextDelegate<string>>();
    private readonly ICosmosDbContextSessionTokenManager<string> _fakeCosmosDbContextSessionTokenManager = A.Fake<ICosmosDbContextSessionTokenManager<string>>();
    private readonly ILoggerFactory _fakeLoggerFactory = A.Fake<ILoggerFactory>();

    [Fact]
    public void CosmosDbClientInterceptor_PassesThroughExceptions()
    {
        var interceptedFakeClient = _proxyGenerator.CreateClassProxyWithTarget(_fakeCosmosClient,
            new CosmosDbClientInterceptor<string>(
                _fakeProxyGenerator, 
                _fakeGetCurrentContextDelegate,
                _fakeCosmosDbContextSessionTokenManager, 
                _fakeLoggerFactory));

        var simulatedException = new InvalidOperationException("It broke");
        A.CallTo(() => _fakeCosmosClient.GetContainer(A<string>._, A<string>._))
            .Throws(simulatedException);

        Assert.Throws<InvalidOperationException>(() => interceptedFakeClient.GetContainer("A", "B"))
            .Should().BeSameAs(simulatedException);
    }
    
}