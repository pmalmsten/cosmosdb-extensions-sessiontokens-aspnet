using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore;

public interface ICosmosDbContextSessionTokenManager<in T>
{
    public string? GetSessionTokenForContextFullyQualifiedContainer(
        T context,
        Uri accountEndpoint,
        string databaseName,
        string containerName);
    
    public void SetSessionTokenForContextAndFullyQualifiedContainer(
        T context,
        Uri accountEndpoint,
        string databaseName,
        string containerName,
        SessionTokenWithSource? sessionToken);

    public void SetSessionTokensForContext(
        T context,
        ConcurrentDictionary<uint, SessionTokenWithSource> containerCodeToSessionTokens);

    public bool TryGetSessionTokensForContext(
        T context,
        [NotNullWhen(true)] out ConcurrentDictionary<uint, SessionTokenWithSource>? containerCodeToSessionTokens);
}