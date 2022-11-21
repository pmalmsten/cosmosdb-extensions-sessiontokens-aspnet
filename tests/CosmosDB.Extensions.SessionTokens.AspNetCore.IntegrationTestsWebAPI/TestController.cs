using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTestsWebAPI;

[Route("[controller]")]
[ApiController]
public class TestController : ControllerBase
{
    private const string TestId = "testId";
    private const string TestPartitionKey = "testPartitionKey";

    private readonly Container _container;

    public TestController(CosmosClient cosmosClient)
    {
        _container = cosmosClient.GetContainer("TestDatabase", "TestContainer");
    }

    [HttpGet]
    public async Task<Document> OkEndpoint()
    {
        return await _container.ReadItemAsync<Document>(TestId, new(TestPartitionKey));
    }
    
    [HttpGet("WriteFollowedByRead")]
    public async Task<Document> WriteFollowedByReadEndpoint()
    {
        await _container.ReplaceItemAsync(new Document(), TestId, new PartitionKey(TestPartitionKey));
        
        return await _container.ReadItemAsync<Document>(TestId, new(TestPartitionKey));
    }
    
    [HttpGet("401")]
    public async Task<IActionResult> UnauthorizedEndpoint()
    {
        await _container.ReadItemAsync<Document>(TestId, new(TestPartitionKey));

        return StatusCode(401);
    }
    
    [HttpGet("403")]
    public async Task<IActionResult> AccessDeniedEndpoint()
    {
        await _container.ReadItemAsync<Document>(TestId, new(TestPartitionKey));

        return StatusCode(403);
    }
}

public record Document;