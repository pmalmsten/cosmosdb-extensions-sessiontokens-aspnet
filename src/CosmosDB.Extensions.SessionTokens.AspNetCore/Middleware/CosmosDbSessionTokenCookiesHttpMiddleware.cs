using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    protected override ConcurrentDictionary<string, string>
        ReadIncomingCosmosDbDatabaseSessionTokensFromHttpRequest(HttpContext context)
    {
        ConcurrentDictionary<string, string> result = new ConcurrentDictionary<string, string>();

        foreach (KeyValuePair<string, string> cookie in context.Request.Cookies
                     .Where(it => it.Key.StartsWith(CosmosDbSessionTokenCookiePrefix)))
        {
            result.AddOrUpdate(DatabaseFromCookieName(cookie.Key), cookie.Value, (_, _) => cookie.Value);
        }

        return result;
    }

    protected override void SetOutgoingCosmosDbSessionTokensOnHttpResponse(HttpContext context,
        IReadOnlyDictionary<string, string> dbNameToSessionTokenDictionary)
    {
        if (!ShouldIncludeCookiesForResponseStatusCode(context.Response.StatusCode))
        {
            return;
        }
        
        foreach (var pair in dbNameToSessionTokenDictionary)
        {
            context.Response.Cookies.Append(CookieNameForDatabase(pair.Key), pair.Value);
        }
    }

    private static string CookieNameForDatabase(string databaseName) =>
        $"{CosmosDbSessionTokenCookiePrefix}{WebUtility.UrlEncode(databaseName)}";

    private static string DatabaseFromCookieName(string cookieName) =>
        WebUtility.UrlDecode(cookieName.Substring(CosmosDbSessionTokenCookiePrefix.Length));
    
    private static bool ShouldIncludeCookiesForResponseStatusCode(int statusCode)
    {
        return statusCode switch
        {
            // Cosmos DB session tokens only have an impact when writes are issued to the database. By default, we
            // assume that unauthenticated/unauthorized calls will not result in any writes to the database - as such,
            // including session tokens in these responses would not have any meaningful impact.
            // Furthermore, since the cookies this middleware adds to HTTP responses include Cosmos DB database names
            // in plaintext, we have an interest in not returning those names to unauthenticated/unauthorized callers.
            401 or 403 => false, 
            _ => true
        };
    }
}