using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDbSessionTokenTracingServices(
        this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpContextAccessor();
        
        serviceCollection.TryAddSingleton<IProxyGenerator>(new ProxyGenerator());
        serviceCollection.TryAddSingleton<CosmosDbClientInterceptor<HttpContext>>();
        serviceCollection.TryAddSingleton<CosmosDbContextSessionTokenManager>();

        serviceCollection.TryAddSingleton<GetCurrentContextDelegate<HttpContext>>(provider =>
        {
            var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
            return () => httpContextAccessor.HttpContext;
        });

        serviceCollection.TryAddSingleton<ICosmosDbContextSessionTokenManager<HttpContext>>(provider =>
            provider.GetRequiredService<CosmosDbContextSessionTokenManager>());

        return serviceCollection;
    }
}