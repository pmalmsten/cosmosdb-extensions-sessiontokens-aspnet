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
        string? sessionToken);

    public void SetSessionTokensForContext(
        HttpContext context,
        ConcurrentDictionary<uint, string> containerCodeToSessionTokens);

    public bool TryGetSessionTokensForHttpContext(
        HttpContext context,
        [NotNullWhen(true)] out ConcurrentDictionary<uint, string>? containerCodeToSessionTokens);
}