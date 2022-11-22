using System;
using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore;

public static class CosmosClientExtensions
{
    public static CosmosClient WithSessionTokenTracing(this CosmosClient cosmosClient, IServiceProvider provider)
    {
        return provider.GetRequiredService<IProxyGenerator>()
            .CreateClassProxyWithTarget(cosmosClient,
                provider.GetRequiredService<CosmosDbClientInterceptor<HttpContext>>());
    }
}