using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore.UnitTests.Interceptors;

public class CosmosDbContainerInterceptorTest
{
    private readonly Container _fakeContainer = A.Fake<Container>();
    private readonly Container _container;

    private readonly GetCurrentContextDelegate<int> _fakeGetCurrentContextDelegate =
        A.Fake<GetCurrentContextDelegate<int>>();

    private readonly ICosmosDbContextSessionTokenManager<int> _fakeCosmosDbContextSessionTokenManager =
        A.Fake<ICosmosDbContextSessionTokenManager<int>>();

    private readonly Uri _dummyAccountEndpoint = A.Dummy<Uri>();
    private readonly string _dummyDatabaseName = A.Dummy<string>();
    private readonly string _dummyContainerName = A.Dummy<string>();
    private readonly int _dummyContext = A.Dummy<int>();
    private readonly string _dummySessionToken = A.Dummy<string>();

    public CosmosDbContainerInterceptorTest()
    {
        _container = new ProxyGenerator().CreateClassProxyWithTarget(
            _fakeContainer,
            new CosmosDbContainerInterceptor<int>(
                _dummyAccountEndpoint,
                _dummyDatabaseName,
                _dummyContainerName,
                _fakeGetCurrentContextDelegate,
                _fakeCosmosDbContextSessionTokenManager,
                A.Dummy<ILogger<CosmosDbContainerInterceptor<int>>>()
            ));

        A.CallTo(() => _fakeGetCurrentContextDelegate()).Returns(_dummyContext);

        A.CallTo(() =>
                _fakeCosmosDbContextSessionTokenManager.GetSessionTokenForContextFullyQualifiedContainer(_dummyContext,
                    _dummyAccountEndpoint, _dummyDatabaseName, _dummyContainerName))
            .Returns(_dummySessionToken);
    }

    [Fact]
    public void DatabaseProperty_ReadPassedThrough()
    {
        var database = A.Dummy<Database>();

        A.CallTo(() => _fakeContainer.Database).Returns(database);
        _container.Database.Should().BeSameAs(database);
    }

    [Fact]
    public void IdProperty_ReadPassedThrough()
    {
        var id = A.Dummy<string>();

        A.CallTo(() => _fakeContainer.Id).Returns(id);
        _container.Id.Should().BeSameAs(id);
    }

    [Fact]
    public void ConflictsProperty_ReadPassedThrough()
    {
        var conflicts = A.Dummy<Conflicts>();

        A.CallTo(() => _fakeContainer.Conflicts).Returns(conflicts);
        _container.Conflicts.Should().BeSameAs(conflicts);
    }

    [Fact]
    public void ScriptsProperty_ReadPassedThrough()
    {
        var scripts = A.Dummy<Scripts>();

        A.CallTo(() => _fakeContainer.Scripts).Returns(scripts);
        _container.Scripts.Should().BeSameAs(scripts);
    }

    [Fact]
    public async Task GetItemAsync_SessionTokenInjectedAndCaptured()
    {
        var itemId = A.Dummy<string>();
        var itemPartitionKey = A.Dummy<PartitionKey>();
        var dummyResult = A.Dummy<ItemResponse<object>>();

        A.CallTo(() =>
                _fakeContainer.ReadItemAsync<object>(itemId, itemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(dummyResult);

        (await _container.ReadItemAsync<object>(itemId, itemPartitionKey))
            .Should().BeSameAs(dummyResult);

        Fake.GetCalls(_fakeContainer)
            .Where(it => it.Method.Name == nameof(Container.ReadItemAsync))
            .Should().ContainSingle().Which.Arguments[2]
            .Should().BeOfType<ItemRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
    }

    [Fact]
    public async Task ReplaceItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        var itemId = A.Dummy<string>();
        var itemPartitionKey = A.Dummy<PartitionKey>();
        var fakeResult = A.Fake<ItemResponse<object>>();
        var newSessionToken = A.Dummy<string>();

        A.CallTo(() =>
                _fakeContainer.ReplaceItemAsync(A<object>._, itemId, itemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(fakeResult);

        A.CallTo(() => fakeResult.Headers.Session).Returns(newSessionToken);

        (await _container.ReplaceItemAsync(A.Dummy<object>(), itemId, itemPartitionKey))
            .Should().BeSameAs(fakeResult);

        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[3]
            .Should().BeOfType<ItemRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);

        A.CallTo(() =>
                _fakeCosmosDbContextSessionTokenManager.SetSessionTokenForContextAndFullyQualifiedContainer(
                    _dummyContext,
                    _dummyAccountEndpoint, _dummyDatabaseName, _dummyContainerName,
                    new SessionTokenWithSource(SessionTokenSource.FromWrite, newSessionToken)))
            .MustHaveHappenedOnceExactly();
    }
}