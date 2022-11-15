using System.Collections.Immutable;
using Castle.DynamicProxy;
using CosmosDb.Extensions.SessionTokens.AspNetCore;
using FakeItEasy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests;

public class BasicTests 
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _testOutputHelper;

    public BasicTests(WebApplicationFactory<Program> factory, ITestOutputHelper testOutputHelper)
    {
        _factory = factory;
        _testOutputHelper = testOutputHelper;
    }

    [Theory]
    [InlineData("/Test")]
    public async Task Get_EndpointsReturnSuccessAndCorrectContentType(string url)
    {
        var fakeCosmos = A.Fake<CosmosClient>();
        var fakeContainer = A.Fake<Container>();

        A.CallTo(() => fakeCosmos.GetContainer("TestDatabase", "TestContainer"))
            .Returns(fakeContainer);


        var mockResponse = A.Fake<ItemResponse<Document>>();
        A.CallTo(() =>
                fakeContainer.ReadItemAsync<Document>(A<string>._, A<PartitionKey>._, A<ItemRequestOptions>._,
                    A<CancellationToken>._))
            .Returns(mockResponse);

        A.CallTo(() => mockResponse.Headers.Session).Returns("1234");
        
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<CosmosClient>(provider => 
                        provider
                            .GetRequiredService<ProxyGenerator>()
                            .CreateClassProxyWithTarget(fakeCosmos, provider.GetRequiredService<CosmosClientInterceptor<HttpContext>>()));
                });
            })
            .CreateClient();

        // Act
        var response = await client.GetAsync(url);
        _testOutputHelper.WriteLine(response.ToString());


        // Assert
        response.EnsureSuccessStatusCode(); // Status Code 200-299
        response.Headers.GetValues("Set-Cookie").Should()
            .Equal(ImmutableList<string>.Empty.Add("csmsdb-TestDatabase=1234; path=/"));
    }
}