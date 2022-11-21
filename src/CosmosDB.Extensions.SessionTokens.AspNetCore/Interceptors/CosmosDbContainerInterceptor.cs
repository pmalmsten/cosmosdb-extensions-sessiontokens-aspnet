using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private enum MethodClassification
    {
        Read,
        Write
    }

    private static readonly IReadOnlyDictionary<string, MethodClassification> CosmosDbMethodNameToClassification =
        ImmutableDictionary<string, MethodClassification>.Empty
            .Add(nameof(Container.CreateItemAsync), MethodClassification.Write)
            .Add(nameof(Container.DeleteContainerAsync), MethodClassification.Write)
            .Add(nameof(Container.DeleteItemAsync), MethodClassification.Write)
            .Add(nameof(Container.PatchItemAsync), MethodClassification.Write)
            .Add(nameof(Container.ReadContainerAsync), MethodClassification.Read)
            .Add(nameof(Container.ReadItemAsync), MethodClassification.Read)
            .Add(nameof(Container.ReplaceContainerAsync), MethodClassification.Write)
            .Add(nameof(Container.ReplaceItemAsync), MethodClassification.Write)
            .Add(nameof(Container.ReplaceThroughputAsync), MethodClassification.Write)
            .Add(nameof(Container.UpsertItemAsync), MethodClassification.Write)
            .Add(nameof(Container.CreateItemStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.DeleteContainerStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.DeleteItemStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.PatchItemStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.ReadContainerStreamAsync), MethodClassification.Read)
            .Add(nameof(Container.ReadContainerStreamAsync), MethodClassification.Read)
            .Add(nameof(Container.ReadItemStreamAsync), MethodClassification.Read)
            .Add(nameof(Container.ReadManyItemsAsync), MethodClassification.Read)
            .Add(nameof(Container.ReplaceContainerStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.ReplaceItemStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.UpsertItemStreamAsync), MethodClassification.Write)
            .Add(nameof(Container.ReadManyItemsStreamAsync), MethodClassification.Read);

    private readonly Uri _accountEndpoint;
    private readonly string _databaseName;
    private readonly string _containerName;
    private readonly GetCurrentContextDelegate<T> _getCurrentContextDelegate;
    private readonly ICosmosDbContextSessionTokenManager<T> _cosmosDbContextSessionTokenManager;
    private readonly ILogger<CosmosDbContainerInterceptor<T>> _logger;

    public CosmosDbContainerInterceptor(
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

    public void Intercept(IInvocation invocation)
    {
        using var scope = _logger.BeginScope(
            "Invocation of {MethodName}, assigned invocation ID {InvocationId}", invocation.Method.Name,
            Guid.NewGuid());
        _logger.LogTrace("Entering invocation");

        try
        {
            SetSessionTokenOnRequestOptionsParameter(invocation);

            _logger.LogTrace("Calling invocation.Proceed()");
            invocation.Proceed();
            _logger.LogTrace("Calling invocation.Proceed() returned normally");

            invocation.ReturnValue = DynamicDispatchAsyncVsSync(invocation.Method, (dynamic)invocation.ReturnValue);
        }
        finally
        {
            _logger.LogTrace("Exiting invocation");
        }
    }

    private void SetSessionTokenOnRequestOptionsParameter(IInvocation invocation)
    {
        _logger.LogDebug("Searching for RequestOptions parameter");

        var parameterValuesWithIndex = invocation.Method.GetParameters()
            .Select((value, i) => (value, i));

        foreach (var (parameterInfo, i) in parameterValuesWithIndex)
        {
            var parameterInfoParameterType = parameterInfo.ParameterType;
            if (!parameterInfoParameterType.IsAssignableTo(typeof(RequestOptions))) continue;

            _logger.LogDebug("Found RequestOptions param at position {RequestOptionsParameterIndex}", i);
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
                    _logger.LogDebug("Current context is not null, getting session token from manager");
                    var sessionTokenForContextAndDatabase =
                        _cosmosDbContextSessionTokenManager.GetSessionTokenForContextFullyQualifiedContainer(
                            currentContext, _accountEndpoint, _databaseName, _containerName);

                    if (sessionTokenForContextAndDatabase != null)
                    {
                        sessionTokenProperty.SetValue(argumentValue, sessionTokenForContextAndDatabase);
                        _logger.LogDebug(
                            "SessionToken property set on RequestOptions param at position {RequestOptionsParameterIndex}",
                            i);
                    }
                    else
                    {
                        _logger.LogDebug("Session token from manager is null, doing nothing");
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

            _logger.LogDebug(
                "RequestOptions param at {RequestOptionsParameterIndex} does not have a SessionToken property - ignoring it",
                i);
        }
    }

    private async Task<TTask> DynamicDispatchAsyncVsSync<TTask>(MethodInfo methodInfo, Task<TTask> returnValueTask)
    {
        // Await result, then call synchronous overload.
        _logger.LogDebug("Return value was a Task<T>, awaiting it before continuing");

        return DynamicDispatchAsyncVsSync(methodInfo, await returnValueTask);
    }

    private TResponse DynamicDispatchAsyncVsSync<TResponse>(MethodInfo methodInfo, TResponse response)
    {
        if (response == null)
        {
            _logger.LogWarning(
                "Return value was unexpectedly null - unable to check for a session token from the Cosmos DB SDK");
            return response;
        }

        return (TResponse)DynamicDispatchReturnValueType(methodInfo, (dynamic)response);
    }

    private Response<TResponse> DynamicDispatchReturnValueType<TResponse>(MethodInfo methodInfo,
        Response<TResponse> response)
    {
        var sessionTokenString = response.Headers.Session;
        _logger.LogDebug("Session token captured from Cosmos DB Response<T>: {SessionToken}", sessionTokenString);

        if (sessionTokenString == null)
        {
            _logger.LogDebug("Session token was null - not saved.");
            return response;
        }

        var currentContext = _getCurrentContextDelegate.Invoke();
        if (currentContext != null)
        {
            var methodClassification = CosmosDbMethodNameToClassification[methodInfo.Name];

            _cosmosDbContextSessionTokenManager.SetSessionTokenForContextAndFullyQualifiedContainer(
                currentContext,
                _accountEndpoint,
                _databaseName,
                _containerName,
                new SessionTokenWithSource(
                    methodClassification switch
                    {
                        MethodClassification.Read => SessionTokenSource.FromRead,
                        MethodClassification.Write => SessionTokenSource.FromWrite,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(methodClassification),
                            methodClassification.ToString())
                    },
                    sessionTokenString
                ));
            _logger.LogDebug("Current context was not null, session token was saved");
        }
        else
        {
            _logger.LogWarning(
                "Current context is null, this call flow is not currently within a tracked context. Session token not saved");
        }

        return response;
    }

    private object DynamicDispatchReturnValueType(MethodInfo methodInfo, object response)
    {
        _logger.LogDebug("Return value was not a Response<> instance - actual type was {ReturnValueType}",
            response.GetType());
        return response;
    }
}