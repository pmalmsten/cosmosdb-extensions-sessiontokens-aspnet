using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public class ContainerInterceptor<T> : IInterceptor
{
    private readonly string _databaseName;
    private readonly IContextAccessor<T> _contextAccessor;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<ContainerInterceptor<T>> _logger;

    public ContainerInterceptor(
        string databaseName,
        IContextAccessor<T> contextAccessor,
        ICosmosDbContextSessionTokenManager<T> cosmosDbContextSessionTokenManager,
        ILogger<ContainerInterceptor<T>> logger)
    {
        _logger = logger;
        _contextAccessor = contextAccessor;
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

                if (_contextAccessor.CurrentContext != null)
                {
                    var sessionTokenForContextAndDatabase =
                        _cosmosDbContextSessionTokenManager.GetSessionTokenForContextAndDatabase(
                            _contextAccessor.CurrentContext, _databaseName);
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

        if (_contextAccessor.CurrentContext != null)
        {
            _cosmosDbContextSessionTokenManager.SetSessionTokenForContextAndDatabase(
                _contextAccessor.CurrentContext, _databaseName, response.Headers.Session);
        }

        return response;
    }

    private object DynamicDispatchReturnValueType(object response)
    {
        _logger.LogInformation("Return value not a Response<> instance: : {ReturnValueType}", response.GetType());
        return response;
    }
}