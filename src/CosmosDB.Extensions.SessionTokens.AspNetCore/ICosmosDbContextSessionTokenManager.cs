using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public interface ICosmosDbContextSessionTokenManager<in T>
{
    public string? GetSessionTokenForContextAndDatabase(T context, string databaseName);
    public void SetSessionTokenForContextAndDatabase(T context, string databaseName, string sessionToken);

    public void SetSessionTokensForContext(HttpContext context,
        ConcurrentDictionary<string, string> databaseNameToSessionTokens);

    public bool TryGetSessionTokensForHttpContext(HttpContext context,
        [NotNullWhen(true)] out ConcurrentDictionary<string, string>? databaseNameToSessionTokens);
}