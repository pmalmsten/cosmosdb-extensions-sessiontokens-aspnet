using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public class CookieCosmosDbHttpMiddleware : CosmosDbSessionTokenHttpMiddleware
{
    private static readonly string CosmosDbSessionTokenCookiePrefix = "csmsdb-";

    public CookieCosmosDbHttpMiddleware(RequestDelegate next,
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
        foreach (var pair in dbNameToSessionTokenDictionary)
        {
            context.Response.Cookies.Append(CookieNameForDatabase(pair.Key), pair.Value);
        }
    }

    private static string CookieNameForDatabase(string databaseName) =>
        $"{CosmosDbSessionTokenCookiePrefix}{WebUtility.UrlEncode(databaseName)}";

    private static string DatabaseFromCookieName(string cookieName) =>
        WebUtility.UrlDecode(cookieName.Substring(CosmosDbSessionTokenCookiePrefix.Length));
}