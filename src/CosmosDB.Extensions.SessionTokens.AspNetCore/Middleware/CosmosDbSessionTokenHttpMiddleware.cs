using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Middleware;

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
                    out var containerCodeToSessionTokenDictionary))
            {
                SetOutgoingCosmosDbSessionTokensOnHttpResponse(context, containerCodeToSessionTokenDictionary);
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Read Cosmos DB session token values (if any) from the incoming request context.
    /// </summary>
    /// <param name="context">The HttpContext to read from.</param>
    /// <returns>A dictionary of calculated Container codes to Cosmos DB session token value.</returns>
    protected abstract ConcurrentDictionary<uint, SessionTokenWithSource>
        ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(
            HttpContext context);

    /// <summary>
    /// Write Cosmos DB session token values (if any) to the outgoing request context.
    /// </summary>
    /// <param name="context">The HttpContext to write to.</param>
    /// <param name="containerCodeToSessionTokenDictionary">A dictionary of calculated Container codes
    /// to Cosmos DB session token value.</param>
    protected abstract void SetOutgoingCosmosDbSessionTokensOnHttpResponse(HttpContext context,
        IReadOnlyDictionary<uint, SessionTokenWithSource> containerCodeToSessionTokenDictionary);
}