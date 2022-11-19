using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;

public class CosmosDbClientInterceptor<T> : IInterceptor
{
    private readonly IProxyGenerator _generator;
    private readonly GetCurrentContextDelegate<T> _contextAccessor;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosDbClientInterceptor<T>> _logger;
    private readonly ILogger<CosmosDbContainerInterceptor<T>> _containerLogger;

    public CosmosDbClientInterceptor(
        IProxyGenerator generator,
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
        using var scope = _logger.BeginScope("Handling invocation for method {CosmosClientInvokedMethodName}", invocation.Method.Name);

        try
        {
            _logger.LogTrace("Calling invocation.Proceed()");
            invocation.Proceed();
            _logger.LogTrace("Invocation.Proceed() returned normally");

            if (invocation.Method.Name == nameof(CosmosClient.GetContainer) &&
                invocation.ReturnValue is Container value)
            {
                _logger.LogTrace("Invocation matches GetContainer, returning proxied Container object");
                invocation.ReturnValue = _generator.CreateClassProxyWithTarget(
                    value,
                    new CosmosDbContainerInterceptor<T>(
                        (string)invocation.Arguments[0],
                        _contextAccessor,
                        _cosmosDbContextSessionTokenManager,
                        _containerLogger));
            }
        }
        catch
        {
            _logger.LogTrace("Invocation.Proceed() threw an exception");
            throw;
        }
    }
}