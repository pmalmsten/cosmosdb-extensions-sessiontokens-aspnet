using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTests;

[Route("[controller]")]
[ApiController]
public class TestController : ControllerBase
{
    private readonly Container _container;

    public TestController(CosmosClient cosmosClient)
    {
        _container = cosmosClient.GetContainer("TestDatabase", "TestContainer");
    }

    [HttpGet(Name = "TestEndpoint")]
    public async Task<Document> TestEndpoint()
    {
        return await _container.ReadItemAsync<Document>("testId", new PartitionKey("testPartitionKey"));
    }
}

public record Document;