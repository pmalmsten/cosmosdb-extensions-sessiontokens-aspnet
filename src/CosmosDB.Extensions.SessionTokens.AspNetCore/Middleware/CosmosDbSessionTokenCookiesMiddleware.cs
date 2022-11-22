using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Middleware;

/// <summary>
/// Preserves Cosmos DB session tokens across HTTP requests by sending them to clients as cookies. Adds one cookie
/// per Cosmos DB database called as part of generating a response to a given HTTP request.
/// </summary>
public class CosmosDbSessionTokenCookiesMiddleware : CosmosDbSessionTokenHttpMiddleware
{
    private static readonly string CosmosDbSessionTokenCookiePrefix = "csmsdb-";

    public CosmosDbSessionTokenCookiesMiddleware(RequestDelegate next,
        ICosmosDbContextSessionTokenManager<HttpContext> sessionTokenManager) : base(next, sessionTokenManager)
    {
    }

    protected override ConcurrentDictionary<uint, SessionTokenWithSource>
        ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(HttpContext context)
    {
        ConcurrentDictionary<uint, SessionTokenWithSource> result = new();

        foreach (KeyValuePair<string, string> cookie in context.Request.Cookies
                     .Where(it => it.Key.StartsWith(CosmosDbSessionTokenCookiePrefix)))
        {
            var containerCodeFromCookieName = ContainerCodeFromCookieName(cookie.Key);
            if (containerCodeFromCookieName != null)
            {
                var newSessionToken = new SessionTokenWithSource(SessionTokenSource.FromIncomingRequest, cookie.Value);
                
                result.AddOrUpdate(
                    containerCodeFromCookieName.Value, 
                    newSessionToken, 
                    (_, _) => newSessionToken);
            }
        }

        return result;
    }

    protected override void SetOutgoingCosmosDbSessionTokensOnHttpResponse(HttpContext context,
        IReadOnlyDictionary<uint, SessionTokenWithSource> containerCodeToSessionTokenDictionary)
    {
        if (!ShouldIncludeCookiesForResponseStatusCode(context.Response.StatusCode))
        {
            return;
        }

        foreach (var pair in containerCodeToSessionTokenDictionary)
        {
            context.Response.Cookies.Append(CookieNameForContainerCode(pair.Key), pair.Value.SessionToken);
        }
    }

    private static string CookieNameForContainerCode(uint containerCode) =>
        $"{CosmosDbSessionTokenCookiePrefix}{containerCode}";

    private static uint? ContainerCodeFromCookieName(string cookieName) =>
        uint.TryParse(cookieName[CosmosDbSessionTokenCookiePrefix.Length..], out var containerCode)
            ? containerCode
            : null;

    private static bool ShouldIncludeCookiesForResponseStatusCode(int statusCode)
    {
        return statusCode switch
        {
            // Cosmos DB session tokens only have an impact when writes are issued to the database. By default, we
            // assume that unauthenticated/unauthorized calls will not result in any writes to the database - as such,
            // including session tokens in these responses would not have any meaningful impact.
            401 or 403 => false,
            _ => true
        };
    }
}