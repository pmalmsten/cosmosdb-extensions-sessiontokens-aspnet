using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;

public delegate T? GetCurrentContextDelegate<out T>();

public class CosmosDbContainerInterceptor<T> : IAsyncInterceptor
{
    private readonly Uri _accountEndpoint;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly GetCurrentContextDelegate<T> _getCurrentContextDelegate;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosDbContainerInterceptor<T>> _logger;

    internal CosmosDbContainerInterceptor(
        Uri accountEndpoint,
        string databaseName,
        string containerName,
        GetCurrentContextDelegate<T> getCurrentContextDelegate,
        ICosmosDbContextSessionTokenManager<T> cosmosDbContextSessionTokenManager,
        ILogger<CosmosDbContainerInterceptor<T>> logger)
    {
        _accountEndpoint = accountEndpoint;
        _logger = logger;
        _getCurrentContextDelegate = getCurrentContextDelegate;
        _cosmosDbContextSessionTokenManager = cosmosDbContextSessionTokenManager;
        _databaseName = databaseName;
        _containerName = containerName;
    }

    public void InterceptSynchronous(IInvocation invocation)
    {
        using var scope = _logger.BeginScope(
            "Invocation of {MethodName}, assigned invocation ID {InvocationId}", invocation.Method.Name,
            Guid.NewGuid());
        _logger.LogTrace("Entering synchronous invocation");

        try
        {
            SetSessionTokenOnRequestOptionsParameter(invocation);

            _logger.LogTrace("Calling invocation.Proceed()");
            invocation.Proceed();
            _logger.LogTrace("Calling invocation.Proceed() returned normally");

            InterceptReturnValue(invocation.Method, invocation.ReturnValue);
        }
        finally
        {
            _logger.LogTrace("Exiting invocation");
        }
    }
    
    public void InterceptAsynchronous(IInvocation invocation)
    {
        using var scope = _logger.BeginScope(
            "Invocation of {MethodName}, assigned invocation ID {InvocationId}", invocation.Method.Name,
            Guid.NewGuid());
        _logger.LogTrace("Entering async invocation with no result");

        try
        {
            invocation.ReturnValue = InterceptAsync(invocation);
        }
        finally
        {
            _logger.LogTrace("Exiting invocation");
        }
        
        async Task InterceptAsync(IInvocation invocation2)
        {
            SetSessionTokenOnRequestOptionsParameter(invocation2);
        
            _logger.LogTrace("Awaiting before continuing");
        
            invocation2.Proceed();
            var task = (Task)invocation2.ReturnValue;
            await task.ConfigureAwait(false);
        }
    }

    public void InterceptAsynchronous<TResult>(IInvocation invocation)
    {
        using var scope = _logger.BeginScope(
            "Invocation of {MethodName}, assigned invocation ID {InvocationId}", invocation.Method.Name,
            Guid.NewGuid());
        _logger.LogTrace("Entering async invocation with result");

        try
        {
            invocation.ReturnValue = InterceptAsyncWithResult<TResult>(invocation);
        }
        finally
        {
            _logger.LogTrace("Exiting invocation");
        }
        
        async Task<TResult2> InterceptAsyncWithResult<TResult2>(IInvocation invocation2)
        {
            SetSessionTokenOnRequestOptionsParameter(invocation2);
        
            _logger.LogTrace("Awaiting before continuing");
        
            invocation2.Proceed();
            var task = (Task<TResult2>)invocation2.ReturnValue;
            var result = await task.ConfigureAwait(false);
        
            return InterceptReturnValue(invocation2.Method, result);
        }
    }

    private void SetSessionTokenOnRequestOptionsParameter(IInvocation invocation)
    {
        _logger.LogTrace("Searching for RequestOptions parameter");

        var parameterValuesWithIndex = invocation.Method.GetParameters()
            .Select((value, i) => (value, i));

        bool sessionTokenInjectedIntoCallParams = false;
        foreach (var (parameterInfo, i) in parameterValuesWithIndex)
        {
            var parameterInfoParameterType = parameterInfo.ParameterType;
            if (!parameterInfoParameterType.IsAssignableTo(typeof(RequestOptions))) continue;

            _logger.LogTrace("Found RequestOptions param at position {RequestOptionsParameterIndex}", i);
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
                    _logger.LogTrace("Current context is not null, getting session token from manager");
                    var sessionTokenForContextAndDatabase =
                        _cosmosDbContextSessionTokenManager.GetSessionTokenForContextFullyQualifiedContainer(
                            currentContext, _accountEndpoint, _databaseName, _containerName);

                    if (sessionTokenForContextAndDatabase != null)
                    {
                        sessionTokenProperty.SetValue(argumentValue, sessionTokenForContextAndDatabase);
                        _logger.LogTrace(
                            "SessionToken property set on RequestOptions param at position {RequestOptionsParameterIndex}",
                            i);
                        sessionTokenInjectedIntoCallParams = true;
                    }
                    else
                    {
                        _logger.LogTrace("Session token from manager is null, doing nothing");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Current context is null, this call flow is not currently within a tracked context. Doing nothing");
                }

                invocation.Arguments[i] = argumentValue;
                break;
            }

            _logger.LogTrace(
                "RequestOptions param at {RequestOptionsParameterIndex} does not have a SessionToken property - ignoring it",
                i);
        }

        _logger.LogDebug("Session Token injected into call parameters: {SessionTokenInjectedIntoCallParams}",
            sessionTokenInjectedIntoCallParams);
    }

    /// <summary>
    /// Intercept the non-<see cref="Task{TResult}"/> return value of the intercepted method call.
    /// </summary>
    /// <remarks>
    /// This method may be invoked by dynamic dispatch at runtime if the intercepted method call directly returns a
    /// non-Task value.
    /// </remarks>
    /// <param name="methodInfo">Info about the method whose invocation was intercepted.</param>
    /// <param name="returnValue">The value returned by the intercepted method call.</param>
    /// <typeparam name="TResult">The type of the returned value.</typeparam>
    /// <returns>The returned value.</returns>
    private TResult InterceptReturnValue<TResult>(MethodInfo methodInfo, TResult returnValue)
    {
        if (returnValue == null)
        {
            _logger.LogWarning(
                "Return value was unexpectedly null - unable to check for a session token from the Cosmos DB SDK");
            return returnValue;
        }

        var returnValueType = returnValue.GetType();
        var returnValueHeadersProperty = returnValueType.GetProperty(nameof(Response<object>.Headers));

        if (returnValueHeadersProperty != null && returnValueHeadersProperty.PropertyType == typeof(Headers))
        {
            _logger.LogDebug("Found Headers property on return value - attempting to capture Session Token value.");
            var headersPropertyValue = (Headers?) returnValueHeadersProperty.GetValue(returnValue);
            var sessionTokenString = headersPropertyValue?.Session;
        
            TrySaveSessionTokenValue(methodInfo, sessionTokenString);
        }
        else
        {
            _logger.LogTrace(
                "Session Token not captured - return value did not have a 'Headers' property. Return value type was {ReturnValueType}",
                returnValueType);
        }

        return returnValue;
    }

    private void TrySaveSessionTokenValue(MethodInfo methodInfo, string? sessionTokenString)
    {
        _logger.LogTrace("Session Token value: {SessionToken}", sessionTokenString);
        
        if (sessionTokenString == null)
        {
            _logger.LogTrace("Session token was null - not saved");
            return;
        }

        var currentContext = _getCurrentContextDelegate.Invoke();
        if (currentContext != null)
        {
            var sessionTokenCapturedFromRead = methodInfo.Name.StartsWith("Read");

            _cosmosDbContextSessionTokenManager.SetSessionTokenForContextAndFullyQualifiedContainer(
                currentContext,
                _accountEndpoint,
                _databaseName,
                _containerName,
                new SessionTokenWithSource(
                    sessionTokenCapturedFromRead ? SessionTokenSource.FromRead : SessionTokenSource.FromWrite,
                    sessionTokenString
                ));
            _logger.LogTrace("Session token was saved");
        }

        _logger.LogWarning(
            "Current context is null, this call flow is not currently within a tracked context. Session token not saved");
    }
}