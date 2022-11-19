using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;

public delegate T? GetCurrentContextDelegate<out T>();

public class CosmosDbContainerInterceptor<T> : IInterceptor
{
    private readonly string _databaseName;
    private readonly GetCurrentContextDelegate<T> _getCurrentContextDelegate;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosDbContainerInterceptor<T>> _logger;

    public CosmosDbContainerInterceptor(
        string databaseName,
        GetCurrentContextDelegate<T> getCurrentContextDelegate,
        ICosmosDbContextSessionTokenManager<T> cosmosDbContextSessionTokenManager,
        ILogger<CosmosDbContainerInterceptor<T>> logger)
    {
        _logger = logger;
        _getCurrentContextDelegate = getCurrentContextDelegate;
        _cosmosDbContextSessionTokenManager = cosmosDbContextSessionTokenManager;
        _databaseName = databaseName;
    }

    public void Intercept(IInvocation invocation)
    {
        using (_logger.BeginScope(new { DatabaseName = _databaseName, InvocationId = Guid.NewGuid() }))
        {
            _logger.LogInformation("Before target call: {TargetType}.{MethodName}", invocation.TargetType,
                invocation.Method.Name);
            try
            {
                SetSessionTokenOnRequestOptionsParameter(invocation);

                invocation.Proceed();

                invocation.ReturnValue = DynamicDispatchAsyncVsSync((dynamic)invocation.ReturnValue);
            }
            finally
            {
                _logger.LogInformation("After target call: {TargetType}.{MethodName}", invocation.TargetType,
                    invocation.Method.Name);
            }
        }
    }

    private void SetSessionTokenOnRequestOptionsParameter(IInvocation invocation)
    {
        var parameterValuesWithIndex = invocation.Method.GetParameters()
            .Select((value, i) => (value, i));

        foreach (var (parameterInfo, i) in parameterValuesWithIndex)
        {
            var parameterInfoParameterType = parameterInfo.ParameterType;
            if (!parameterInfoParameterType.IsAssignableTo(typeof(RequestOptions))) continue;

            _logger.LogInformation("Found RequestOptions param at position {ParamIndex}", i);
            PropertyInfo? sessionTokenProperty =
                parameterInfoParameterType.GetProperty(nameof(ItemRequestOptions.SessionToken));

            if (sessionTokenProperty != null)
            {
                object argumentValue = invocation.Arguments[i] ??
                                       Activator.CreateInstance(parameterInfoParameterType) ??
                                       throw new InvalidOperationException(
                                           $"Unable to create default instance of {parameterInfoParameterType}");

                var currentContext = _getCurrentContextDelegate.Invoke();
                if (currentContext != null)
                {
                    var sessionTokenForContextAndDatabase =
                        _cosmosDbContextSessionTokenManager.GetSessionTokenForContextAndDatabase(
                            currentContext, _databaseName);
                    if (sessionTokenForContextAndDatabase != null)
                    {
                        sessionTokenProperty.SetValue(argumentValue, sessionTokenForContextAndDatabase);
                    }
                }

                invocation.Arguments[i] = argumentValue;

                _logger.LogInformation("SessionToken property set - done checking parameters");
                break;
            }

            _logger.LogInformation("This RequestOptions param not have a SessionToken Property - ignored");
        }
    }

    private async Task<TTask> DynamicDispatchAsyncVsSync<TTask>(Task<TTask> response)
    {
        // Await result, then call synchronous overload.
        _logger.LogInformation("Task returned, awaiting it first");

        return DynamicDispatchAsyncVsSync(await response);
    }

    private TResponse DynamicDispatchAsyncVsSync<TResponse>(TResponse response)
    {
        return response == null ? response : (TResponse)DynamicDispatchReturnValueType((dynamic)response);
    }

    private Response<TResponse> DynamicDispatchReturnValueType<TResponse>(Response<TResponse> response)
    {
        _logger.LogInformation("Session: {Session}", response.Headers.Session);

        var currentContext = _getCurrentContextDelegate.Invoke();
        if (currentContext != null)
        {
            _cosmosDbContextSessionTokenManager.SetSessionTokenForContextAndDatabase(
                currentContext, _databaseName, response.Headers.Session);
        }

        return response;
    }

    private object DynamicDispatchReturnValueType(object response)
    {
        _logger.LogInformation("Return value not a Response<> instance: : {ReturnValueType}", response.GetType());
        return response;
    }
}