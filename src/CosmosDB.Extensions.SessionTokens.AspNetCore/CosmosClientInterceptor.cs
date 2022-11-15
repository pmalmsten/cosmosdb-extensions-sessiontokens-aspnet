using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public class CosmosClientInterceptor<T> : IInterceptor
{
    private readonly ProxyGenerator _generator;
    private readonly IContextAccessor<T> _contextAccessor;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosClientInterceptor<T>> _logger;
    private readonly ILogger<ContainerInterceptor<T>> _containerLogger;

    public CosmosClientInterceptor(
        ProxyGenerator generator,
        IContextAccessor<T> contextAccessor,
        ICosmosDbContextSessionTokenManager<T> cosmosDbContextSessionTokenManager,
        ILoggerFactory loggerFactory)
    {
        _generator = generator;
        _logger = loggerFactory.CreateLogger<CosmosClientInterceptor<T>>();
        _containerLogger = loggerFactory.CreateLogger<ContainerInterceptor<T>>();
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
                new ContainerInterceptor<T>(
                    (string)invocation.Arguments[0],
                    _contextAccessor,
                    _cosmosDbContextSessionTokenManager,
                    _containerLogger));
        }
    }
}