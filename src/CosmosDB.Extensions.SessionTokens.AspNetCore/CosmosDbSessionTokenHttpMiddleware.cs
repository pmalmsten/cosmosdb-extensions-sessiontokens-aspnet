using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public abstract class CosmosDbSessionTokenHttpMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICosmosDbContextSessionTokenManager<HttpContext> _sessionTokenManager;

    protected CosmosDbSessionTokenHttpMiddleware(
        RequestDelegate next,
        ICosmosDbContextSessionTokenManager<HttpContext> sessionTokenManager)
    {
        _next = next;
        _sessionTokenManager = sessionTokenManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        _sessionTokenManager.SetSessionTokensForContext(context,
            ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(context));

        context.Response.OnStarting(() =>
        {
            if (_sessionTokenManager.TryGetSessionTokensForHttpContext(context,
                    out var databaseNameToSessionTokenDictionary))
            {
                SetOutgoingCosmosDbSessionTokensOnHttpResponse(context, databaseNameToSessionTokenDictionary);
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    protected abstract ConcurrentDictionary<string, string>
        ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(
            HttpContext context);

    protected abstract void SetOutgoingCosmosDbSessionTokensOnHttpResponse(HttpContext context,
        IReadOnlyDictionary<string, string> dbNameToSessionTokenDictionary);
}