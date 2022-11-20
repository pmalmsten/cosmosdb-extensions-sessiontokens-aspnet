using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.Middleware;

/// <summary>
/// Preserves Cosmos DB session tokens across HTTP requests by sending them to clients as cookies. Adds one cookie
/// per Cosmos DB database called as part of generating a response to a given HTTP request.
/// </summary>
public class CosmosDbSessionTokenCookiesHttpMiddleware : CosmosDbSessionTokenHttpMiddleware
{
    private static readonly string CosmosDbSessionTokenCookiePrefix = "csmsdb-";

    public CosmosDbSessionTokenCookiesHttpMiddleware(RequestDelegate next,
        ICosmosDbContextSessionTokenManager<HttpContext> sessionTokenManager) : base(next, sessionTokenManager)
    {
    }

    protected override ConcurrentDictionary<uint, string>
        ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(HttpContext context)
    {
        ConcurrentDictionary<uint, string> result = new ConcurrentDictionary<uint, string>();

        foreach (KeyValuePair<string, string> cookie in context.Request.Cookies
                     .Where(it => it.Key.StartsWith(CosmosDbSessionTokenCookiePrefix)))
        {
            var containerCodeFromCookieName = ContainerCodeFromCookieName(cookie.Key);
            if (containerCodeFromCookieName != null)
            {
                result.AddOrUpdate(containerCodeFromCookieName.Value, cookie.Value, (_, _) => cookie.Value);
            }
        }

        return result;
    }

    protected override void SetOutgoingCosmosDbSessionTokensOnHttpResponse(HttpContext context,
        IReadOnlyDictionary<uint, string> containerCodeToSessionTokenDictionary)
    {
        if (!ShouldIncludeCookiesForResponseStatusCode(context.Response.StatusCode))
        {
            return;
        }

        foreach (var pair in containerCodeToSessionTokenDictionary)
        {
            context.Response.Cookies.Append(CookieNameForContainerCode(pair.Key), pair.Value);
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