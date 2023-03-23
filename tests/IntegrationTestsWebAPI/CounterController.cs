using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTestsWebAPI;

[Route("[controller]")]
[ApiController]
public class CounterController : ControllerBase
{
    private const string CounterId = "Counter1";
    
    private readonly Container _countersContainer;

    public CounterController(CosmosClient cosmosClient)
    {
        _countersContainer = cosmosClient.GetContainer("Sandbox", "Counters");
    }

    [HttpGet]
    public async Task<Counter> Get() =>
        (await _countersContainer
            .ReadItemAsync<Counter>(
                CounterId,
                new PartitionKey(CounterId)))
        .Resource;

    [HttpPost]
    public async Task<Counter> Increment()
    {
        try
        {
            return await IncrementCounter();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await CreateCounter();
        }
    }
    
    private async Task<Counter> IncrementCounter()
    {
        var readResponse = await _countersContainer.ReadItemAsync<Counter>(CounterId, new PartitionKey(CounterId));
        var currentCounterValue = readResponse.Resource;

        var replaceResponse = await _countersContainer.ReplaceItemAsync(
            currentCounterValue with { Count = currentCounterValue.Count + 1 },
            currentCounterValue.Id,
            new PartitionKey(currentCounterValue.Id),
            new ItemRequestOptions()
            {
                IfMatchEtag = readResponse.ETag
            });

        return replaceResponse.Resource;
    }

    private async Task<Counter> CreateCounter() =>
        (await _countersContainer.CreateItemAsync(
            new Counter(CounterId, 0),
            new PartitionKey(CounterId)))
        .Resource;
}

public record Counter(
    [property:JsonProperty("id")] string Id, 
    [property:JsonProperty("count")] int Count);