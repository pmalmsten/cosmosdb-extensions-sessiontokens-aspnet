using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Xunit;
using Xunit.Abstractions;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore.UnitTests.Interceptors;

public class CosmosDbContainerInterceptorTest
{
    private readonly ITestOutputHelper _testOutputHelper;
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
    private readonly string _dummyNewSessionToken = A.Dummy<string>();
    private readonly string _dummyItemId = A.Dummy<string>();
    private readonly PartitionKey _dummyItemPartitionKey = A.Dummy<PartitionKey>();
    private readonly ItemResponse<object> _dummyItemResponse = A.Dummy<ItemResponse<object>>();
    private readonly ResponseMessage _dummyResponseMessage = A.Fake<ResponseMessage>();

    public CosmosDbContainerInterceptorTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _container = new ProxyGenerator().CreateClassProxyWithTarget(
            _fakeContainer,
            new CosmosDbContainerInterceptor<int>(
                _dummyAccountEndpoint,
                _dummyDatabaseName,
                _dummyContainerName,
                _fakeGetCurrentContextDelegate,
                _fakeCosmosDbContextSessionTokenManager,
                testOutputHelper.BuildLoggerFor<CosmosDbContainerInterceptor<int>>()
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
        A.CallTo(() =>
                _fakeContainer.ReadItemAsync<object>(_dummyItemId, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        (await _container.ReadItemAsync<object>(_dummyItemId, _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(2);
    }

    [Fact]
    public async Task ReplaceItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReplaceItemAsync(A<object>._, _dummyItemId, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        A.CallTo(() => _dummyItemResponse.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.ReplaceItemAsync(A.Dummy<object>(), _dummyItemId, _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(3);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task ReplaceItemStreamAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReplaceItemStreamAsync(A<Stream>._, _dummyItemId, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        A.CallTo(() => _dummyResponseMessage.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.ReplaceItemStreamAsync(A.Dummy<Stream>(), _dummyItemId, _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyResponseMessage);
        
        AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(3);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task CreateItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.CreateItemAsync(A<object>._, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        A.CallTo(() => _dummyItemResponse.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.CreateItemAsync(A.Dummy<object>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task CreateItemStreamAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.CreateItemStreamAsync(A<Stream>._, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        A.CallTo(() => _dummyResponseMessage.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.CreateItemStreamAsync(A.Dummy<Stream>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyResponseMessage);

        AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    private void AssertContainerMethodCallIncludesSessionTokenInParamAtIndex(int paramIndex)
    {
        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[paramIndex]
            .Should().BeOfType<ItemRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
    }

    private void AssertSessionTokenSavedFromResponse(SessionTokenSource expectedSessionTokenSource,
        string expectedSessionTokenValue)
    {
        A.CallTo(() =>
                _fakeCosmosDbContextSessionTokenManager.SetSessionTokenForContextAndFullyQualifiedContainer(
                    _dummyContext,
                    _dummyAccountEndpoint, _dummyDatabaseName, _dummyContainerName,
                    new SessionTokenWithSource(expectedSessionTokenSource, expectedSessionTokenValue)))
            .MustHaveHappenedOnceExactly();
    }
}