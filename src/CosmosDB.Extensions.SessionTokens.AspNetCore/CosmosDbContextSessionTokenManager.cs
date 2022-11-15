using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public class CosmosDbContextSessionTokenManager : ICosmosDbContextSessionTokenManager<HttpContext>
{
    private readonly ConditionalWeakTable<HttpContext, ConcurrentDictionary<string, string>>
        _httpContextToDatabaseSessionTokenDictionary = new();

    public string? GetSessionTokenForContextAndDatabase(HttpContext context, string databaseName)
    {
        string? result = null;

        if (_httpContextToDatabaseSessionTokenDictionary.TryGetValue(context, out var databaseSessionTokenMap))
        {
            databaseSessionTokenMap.TryGetValue(databaseName, out result);
        }

        return result;
    }

    public void SetSessionTokenForContextAndDatabase(HttpContext context, string databaseName, string sessionToken)
    {
        _httpContextToDatabaseSessionTokenDictionary
            .GetOrCreateValue(context)
            .AddOrUpdate(databaseName, sessionToken, (_, _) => sessionToken);
    }

    public void SetSessionTokensForContext(HttpContext context,
        ConcurrentDictionary<string, string> databaseNameToSessionTokens)
    {
        _httpContextToDatabaseSessionTokenDictionary.Add(context, databaseNameToSessionTokens);
    }

    public bool TryGetSessionTokensForHttpContext(HttpContext context,
        [NotNullWhen(true)] out ConcurrentDictionary<string, string>? databaseNameToSessionTokens)
    {
        return _httpContextToDatabaseSessionTokenDictionary.TryGetValue(context, out databaseNameToSessionTokens);
    }
}