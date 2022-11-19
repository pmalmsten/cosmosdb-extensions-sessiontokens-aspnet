using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;

public class CosmosDbClientInterceptor<T> : IInterceptor
{
    private readonly ProxyGenerator _generator;
    private readonly GetCurrentContextDelegate<T> _contextAccessor;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosDbClientInterceptor<T>> _logger;
    private readonly ILogger<CosmosDbContainerInterceptor<T>> _containerLogger;

    public CosmosDbClientInterceptor(
        ProxyGenerator generator,
        GetCurrentContextDelegate<T> contextAccessor,
        ICosmosDbContextSessionTokenManager<T> cosmosDbContextSessionTokenManager,
        ILoggerFactory loggerFactory)
    {
        _generator = generator;
        _logger = loggerFactory.CreateLogger<CosmosDbClientInterceptor<T>>();
        _containerLogger = loggerFactory.CreateLogger<CosmosDbContainerInterceptor<T>>();
        _contextAccessor = contextAccessor;
        _cosmosDbContextSessionTokenManager = cosmosDbContextSessionTokenManager;
    }

    public void Intercept(IInvocation invocation)
    {
        _logger.LogTrace("Incoming invocation: {TypeName}.{MethodName}", invocation.TargetType.Name,
            invocation.Method.Name);
        invocation.Proceed();

        if (invocation.Method.Name == nameof(CosmosClient.GetContainer) &&
            invocation.ReturnValue is Container value)
        {
            _logger.LogTrace("Invocation matches {GetContainerMethodName}, applying nested interceptor",
                nameof(CosmosClient.GetContainer));
            invocation.ReturnValue = _generator.CreateClassProxyWithTarget(
                value,
                new CosmosDbContainerInterceptor<T>(
                    (string)invocation.Arguments[0],
                    _contextAccessor,
                    _cosmosDbContextSessionTokenManager,
                    _containerLogger));
        }
    }
}