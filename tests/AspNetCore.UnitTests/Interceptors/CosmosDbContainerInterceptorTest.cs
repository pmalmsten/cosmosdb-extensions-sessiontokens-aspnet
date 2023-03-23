using System.Collections.Immutable;
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
    private readonly FeedResponse<object> _dummyFeedResponse = A.Dummy<FeedResponse<object>>();

    public CosmosDbContainerInterceptorTest(ITestOutputHelper testOutputHelper)
    {
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
    public async Task ReadItemAsync_SessionTokenInjectedAndCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReadItemAsync<object>(_dummyItemId, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        (await _container.ReadItemAsync<object>(_dummyItemId, _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
    }
    
    [Fact]
    public async Task ReadItemStreamAsync_SessionTokenInjectedAndCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReadItemStreamAsync(_dummyItemId, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        (await _container.ReadItemStreamAsync(_dummyItemId, _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyResponseMessage);

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
    }
    
    [Fact]
    public async Task ReadManyItemsAsync_SessionTokenInjectedAndCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReadManyItemsAsync<object>(A<IReadOnlyList<(string, PartitionKey)>>._, A<ReadManyRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyFeedResponse);

        (await _container.ReadManyItemsAsync<object>(ImmutableList.Create((_dummyItemId, _dummyItemPartitionKey))))
            .Should().BeSameAs(_dummyFeedResponse);

        AssertReadManyRequestOptionsIncludesSessionTokenAtIndex(1);
    }
    
    [Fact]
    public async Task ReadManyItemsStreamAsync_SessionTokenInjectedAndCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.ReadManyItemsStreamAsync(A<IReadOnlyList<(string, PartitionKey)>>._, A<ReadManyRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        (await _container.ReadManyItemsStreamAsync(ImmutableList.Create((_dummyItemId, _dummyItemPartitionKey))))
            .Should().BeSameAs(_dummyResponseMessage);

        AssertReadManyRequestOptionsIncludesSessionTokenAtIndex(1);
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

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(3);
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
        
        AssertItemRequestOptionsIncludesSessionTokenAtIndex(3);
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

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
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

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }
    
    [Fact]
    public async Task DeleteItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.DeleteItemAsync<object>(A<string>._, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        A.CallTo(() => _dummyItemResponse.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.DeleteItemAsync<object>(A.Dummy<string>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task DeleteItemStreamAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.DeleteItemStreamAsync(A<string>._, _dummyItemPartitionKey, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        A.CallTo(() => _dummyResponseMessage.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.DeleteItemStreamAsync(A.Dummy<string>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyResponseMessage);

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }
    
    [Fact]
    public async Task PatchItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.PatchItemAsync<object>(
                    A<string>._,
                    _dummyItemPartitionKey,
                    A<IReadOnlyList<PatchOperation>>._,
                    A<PatchItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        A.CallTo(() => _dummyItemResponse.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.PatchItemAsync<object>(
                A.Dummy<string>(), _dummyItemPartitionKey, ImmutableList<PatchOperation>.Empty))
            .Should().BeSameAs(_dummyItemResponse);

        AssertPatchItemRequestOptionsIncludesSessionTokenAtIndex(3);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task PatchItemStreamAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.PatchItemStreamAsync(
                    A<string>._,
                    _dummyItemPartitionKey,
                    A<IReadOnlyList<PatchOperation>>._,
                    A<PatchItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        A.CallTo(() => _dummyResponseMessage.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.PatchItemStreamAsync(A.Dummy<string>(), A.Dummy<PartitionKey>(), ImmutableList<PatchOperation>.Empty))
            .Should().BeSameAs(_dummyResponseMessage);

        AssertPatchItemRequestOptionsIncludesSessionTokenAtIndex(3);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }
    
    [Fact]
    public async Task UpsertItemAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.UpsertItemAsync(
                    A<object>._, _dummyItemPartitionKey, A<ItemRequestOptions>._, A<CancellationToken>._))
            .Returns(_dummyItemResponse);

        A.CallTo(() => _dummyItemResponse.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.UpsertItemAsync(A.Dummy<object>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyItemResponse);

        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }

    [Fact]
    public async Task UpsertItemStreamAsync_SessionTokenInjectedAndNewSessionTokenCaptured()
    {
        A.CallTo(() =>
                _fakeContainer.UpsertItemStreamAsync(
                    A<Stream>._, _dummyItemPartitionKey, A<ItemRequestOptions>._, A<CancellationToken>._))
            .Returns(_dummyResponseMessage);

        A.CallTo(() => _dummyResponseMessage.Headers.Session).Returns(_dummyNewSessionToken);

        (await _container.UpsertItemStreamAsync(A.Dummy<Stream>(), _dummyItemPartitionKey))
            .Should().BeSameAs(_dummyResponseMessage);
        
        AssertItemRequestOptionsIncludesSessionTokenAtIndex(2);
        AssertSessionTokenSavedFromResponse(SessionTokenSource.FromWrite, _dummyNewSessionToken);
    }
    
    [Fact]
    public void GetItemLinqQueryable_SessionTokenInjected()
    {
        _container.GetItemLinqQueryable<object>();

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(2);
    }

    [Fact]
    public void GetItemQueryIterator_QueryDefinitionOverload_SessionTokenInjected()
    { 
        _container.GetItemQueryIterator<object>(new QueryDefinition("SELECT * from test t"));

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(2);
    }
    
    [Fact]
    public void GetItemQueryIterator_StringOverload_SessionTokenInjected()
    { 
        _container.GetItemQueryIterator<object>(A.Dummy<string>());

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(2);
    }

    [Fact]
    public void GetItemQueryIterator_FeedRangeOverload_SessionTokenInjected()
    {
        _container.GetItemQueryIterator<object>(A.Dummy<FeedRange>(), new QueryDefinition("SELECT * from test t"));

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(3);
    }
    
    [Fact]
    public void GetItemQueryStreamIterator_QueryDefinitionOverload_SessionTokenInjected()
    { 
        _container.GetItemQueryStreamIterator(new QueryDefinition("SELECT * from test t"));

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(2);
    }
    
    [Fact]
    public void GetItemQueryStreamIterator_StringOverload_SessionTokenInjected()
    { 
        _container.GetItemQueryStreamIterator(A.Dummy<string>());

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(2);
    }

    [Fact]
    public void GetItemQueryStreamIterator_FeedRangeOverload_SessionTokenInjected()
    {
        _container.GetItemQueryStreamIterator(
            A.Dummy<FeedRange>(), new QueryDefinition("SELECT * from test t"), continuationToken: A.Dummy<string>());

        AssertQueryRequestOptionsIncludesSessionTokenAtIndex(3);
    }

    private void AssertQueryRequestOptionsIncludesSessionTokenAtIndex(int paramIndex)
    {
        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[paramIndex]
            .Should().BeAssignableTo<QueryRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
    }

    private void AssertItemRequestOptionsIncludesSessionTokenAtIndex(int paramIndex)
    {
        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[paramIndex]
            .Should().BeAssignableTo<ItemRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
    }
    
    private void AssertPatchItemRequestOptionsIncludesSessionTokenAtIndex(int paramIndex)
    {
        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[paramIndex]
            .Should().BeAssignableTo<PatchItemRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
    }
    
    private void AssertReadManyRequestOptionsIncludesSessionTokenAtIndex(int paramIndex)
    {
        Fake.GetCalls(_fakeContainer)
            .Should().ContainSingle().Which.Arguments[paramIndex]
            .Should().BeAssignableTo<ReadManyRequestOptions>().Which.SessionToken.Should().Be(_dummySessionToken);
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