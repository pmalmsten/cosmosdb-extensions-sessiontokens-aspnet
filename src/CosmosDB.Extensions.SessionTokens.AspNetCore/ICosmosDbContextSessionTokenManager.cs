using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;

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
        HttpContext context,
        ConcurrentDictionary<uint, SessionTokenWithSource> containerCodeToSessionTokens);

    public bool TryGetSessionTokensForHttpContext(
        HttpContext context,
        [NotNullWhen(true)] out ConcurrentDictionary<uint, SessionTokenWithSource>? containerCodeToSessionTokens);
}