using System.Collections.Immutable;
using Castle.DynamicProxy;
using CosmosDb.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTestsWebAPI;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests;

public class BasicCookieIntegrationTests 
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _testOutputHelper;
    
    private readonly CosmosClient _fakeCosmos = A.Fake<CosmosClient>();
    private readonly Container _fakeContainer = A.Fake<Container>();

    public BasicCookieIntegrationTests(WebApplicationFactory<Program> factory, ITestOutputHelper testOutputHelper)
    {
        _factory = factory;
        _testOutputHelper = testOutputHelper;
        
        A.CallTo(() => _fakeCosmos.GetContainer("TestDatabase", "TestContainer"))
            .Returns(_fakeContainer);
    }

    [Fact]
    public async Task SessionTokenFromCosmosDBToCookie_ReadItemAsync()
    {
        var mockResponse = A.Fake<ItemResponse<Document>>();
        A.CallTo(() =>
                _fakeContainer.ReadItemAsync<Document>(A<string>._, A<PartitionKey>._, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(mockResponse);

        A.CallTo(() => mockResponse.Headers.Session).Returns("1234");
        
        // Arrange
        var client = CreateHttpClientWithMockedCosmos();

        // Act
        var response = await client.GetAsync("Test");
        _testOutputHelper.WriteLine(response.ToString());

        // Assert
        response.EnsureSuccessStatusCode(); // Status Code 200-299
        response.Headers.GetValues("Set-Cookie").Should()
            .Equal(ImmutableList<string>.Empty.Add("csmsdb-TestDatabase=1234; path=/"));
    }

    [Fact]
    public async Task SessionTokenFromCookieToCosmosDB_ReadItemAsync()
    {
        // Arrange
        var client = CreateHttpClientWithMockedCosmos();

        var sessionTokenInIncomingCookie = "56678";
        A.CallTo(() => _fakeContainer.ReadItemAsync<Document>(A<string>._, A<PartitionKey>._, A<ItemRequestOptions>._,
                A<CancellationToken>._))
            .Invokes(call =>
            {
                call.Arguments[2].Should().BeOfType<ItemRequestOptions>()
                    .Which
                    .SessionToken.Should().Be(sessionTokenInIncomingCookie);
            })
            .ReturnsLazily(call =>
            {
                var fakeItemResponse = A.Fake<ItemResponse<Document>>();
                A.CallTo(() => fakeItemResponse.Headers.Session)
                    .Returns(sessionTokenInIncomingCookie);

                return Task.FromResult(fakeItemResponse);
            });

        // Act
        var message = new HttpRequestMessage(HttpMethod.Get, "Test");
        message.Headers.Add("Cookie", $"csmsdb-TestDatabase={sessionTokenInIncomingCookie}; path=/");

        var response = await client.SendAsync(message);

        A.CallTo(() => _fakeContainer.ReadItemAsync<Document>(A<string>._, A<PartitionKey>._, A<ItemRequestOptions>._,
                A<CancellationToken>._))
            .MustHaveHappened();

        _testOutputHelper.WriteLine(response.ToString());

        // Assert
        response.EnsureSuccessStatusCode(); // Status Code 200-299
        response.Headers.GetValues("Set-Cookie").Should()
            .Equal(ImmutableList<string>.Empty.Add($"csmsdb-TestDatabase={sessionTokenInIncomingCookie}; path=/"));
    }

    [Fact]
    public async Task SessionTokenFromCookieToCosmosDB_ReadItemAsync_ParallelRequestsSetCorrespondingSessionOnResponses()
    {
        // Arrange
        // Disable client cookie handling to prevent client from 'helpfully' saving the cookie from a prior response and
        // using that cookie value in future requests instead of the cookies we explicitly set for each parallel request.
        var client = CreateHttpClientWithMockedCosmos(handleCookies: false); 
        
        A.CallTo(() => _fakeContainer.ReadItemAsync<Document>(A<string>._, A<PartitionKey>._, A<ItemRequestOptions>._,
                A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                var fakeItemResponse = A.Fake<ItemResponse<Document>>();
                A.CallTo(() => fakeItemResponse.Headers.Session)
                    .Returns(call.Arguments[2].Should().BeOfType<ItemRequestOptions>().Which.SessionToken);

                return Task.FromResult(fakeItemResponse);
            });

        // Act
        var sentRequests = Enumerable.Range(0, 50)
            .Select(it => Guid.NewGuid())
            .Select(sessionToken => (SessionToken: sessionToken, Request: new HttpRequestMessage(HttpMethod.Get, "Test")
            {
                Headers =
                {
                    { "Cookie", $"csmsdb-TestDatabase={sessionToken}; path=/" }
                }
            }))
            .Select(tokenToRequestMessage =>
                (tokenToRequestMessage.SessionToken, ResponseTask: client.SendAsync(tokenToRequestMessage.Request)))
            .ToList(); // We need to eagerly consume the enumerable in order for the requests to be sent in parallel

        // Assert
        foreach (var (sessionToken, responseTask) in sentRequests)
        {
            var response = await responseTask;
            
            _testOutputHelper.WriteLine(response.ToString());
            response.EnsureSuccessStatusCode(); // Status Code 200-299
            response.Headers.GetValues("Set-Cookie").Should()
                .Equal(ImmutableList<string>.Empty.Add($"csmsdb-TestDatabase={sessionToken}; path=/"));
        }

    }


    private HttpClient CreateHttpClientWithMockedCosmos(bool handleCookies = true)
    {
        return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<CosmosClient>(provider =>
                        provider
                            .GetRequiredService<ProxyGenerator>()
                            .CreateClassProxyWithTarget(_fakeCosmos,
                                provider.GetRequiredService<CosmosDbClientInterceptor<HttpContext>>()));
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions()
            {
                HandleCookies = handleCookies
            });
    }
}