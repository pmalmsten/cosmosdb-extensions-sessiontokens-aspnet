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

            // Dynamically dispatch this call at runtime in order to properly handle 1) return values of Task<T>
            // that need to be awaited, and 2) all other return values that don't need to be awaited.
            // For Task<T> return values, we must update the return value here in order to guarantee that
            // `await`ing application code only resumes *after* this interceptor has processed the return value within
            // the Task; this call will return a new Task representing the completion of processing the original return
            // value.
            invocation.ReturnValue = InterceptReturnValue(invocation.Method, (dynamic)invocation.ReturnValue);
        }
        finally
        {
            _logger.LogTrace("Exiting invocation");
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
    /// If the return value is <see cref="Task{TResult}"/>, dynamic dispatch will call this method at runtime. This method
    /// simply awaits the task, and calls the non-Task overload
    /// <see cref="InterceptReturnValue{TNonTask}(System.Reflection.MethodInfo,TNonTask)"/> with the awaited value.
    /// </summary>
    /// <param name="methodInfo">Info about the method whose invocation was intercepted.</param>
    /// <param name="returnValueTask">The Task returned by the intercepted method call.</param>
    /// <typeparam name="TTask">The type of the value within the <see cref="Task{TResult}"/>.</typeparam>
    /// <returns>A new <see cref="Task{TResult}"/> which completes when 1) the given return value Task completes,
    /// and 2) the value contained within that <see cref="Task{TResult}"/> has been processed by this interceptor.</returns>
    private async Task<TTask> InterceptReturnValue<TTask>(MethodInfo methodInfo, Task<TTask> returnValueTask)
    {
        _logger.LogTrace("Return value was a Task<T>, awaiting it before continuing");

        return InterceptReturnValue(methodInfo, await returnValueTask);
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
    /// <typeparam name="TNonTask">The type of the returned value.</typeparam>
    /// <returns>The returned value.</returns>
    private TNonTask InterceptReturnValue<TNonTask>(MethodInfo methodInfo, TNonTask returnValue)
    {
        if (returnValue == null)
        {
            _logger.LogWarning(
                "Return value was unexpectedly null - unable to check for a session token from the Cosmos DB SDK");
            return returnValue;
        }

        // Second dynamic dispatch in order to handle return values that are either 1) of type Response<T>, or 2)
        // anything else.
        bool sessionTokenSaved = TrySaveSessionTokenFromReturnValue(methodInfo, (dynamic)returnValue);

        _logger.LogDebug("Session Token captured from return value: {SessionTokenCapturedFromResponse}",
            sessionTokenSaved);
        return returnValue;
    }

    /// <summary>
    /// Try to save the Cosmos DB session token from the given <see cref="Response{T}"/>.
    /// </summary>
    /// <remarks>The correct overload of this method is selected at runtime by dynamic dispatch.</remarks>
    /// <param name="methodInfo">Info about the method whose invocation was intercepted.</param>
    /// <param name="response">The value returned by the intercepted method call.</param>
    /// <typeparam name="TResponse">The type of value within the <see cref="Response{T}"/></typeparam>
    /// <returns><c>true</c> if a session token was successfully saved; <c>false</c> otherwise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the method call on <see cref="Container"/> is not
    /// recognized.</exception>
    private bool TrySaveSessionTokenFromReturnValue<TResponse>(
        MethodInfo methodInfo,
        Response<TResponse> response)
    {
        var sessionTokenString = response.Headers.Session;
        _logger.LogTrace("Session token captured from Cosmos DB Response<T>: {SessionToken}", sessionTokenString);

        if (sessionTokenString == null)
        {
            _logger.LogTrace("Session token was null - not saved");
            return false;
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
            _logger.LogTrace("Current context was not null, session token was saved");
            return true;
        }

        _logger.LogWarning(
            "Current context is null, this call flow is not currently within a tracked context. Session token not saved");
        return false;
    }

    /// <summary>
    /// Alternate overload for <see cref="TrySaveSessionTokenFromReturnValue{TResponse}"/> in the event that the
    /// return value for the intercepted method call is not of type <see cref="Response{T}"/>.
    /// </summary>
    /// <remarks>The correct overload of this method is selected at runtime by dynamic dispatch.</remarks>
    /// <param name="methodInfo">Info about the method whose invocation was intercepted.</param>
    /// <param name="response">The value returned by the intercepted method call.</param>
    /// <typeparam name="TResponse">The type of value within the <see cref="Response{T}"/></typeparam>
    /// <returns><c>true</c> if a session token was successfully saved; <c>false</c> otherwise.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the method call on <see cref="Container"/> is not
    /// recognized.</exception>
    private bool TrySaveSessionTokenFromReturnValue(MethodInfo methodInfo, object returnValue)
    {
        _logger.LogTrace("Return value was not a Response<> instance - actual type was {ReturnValueType}",
            returnValue.GetType());
        return false;
    }
}