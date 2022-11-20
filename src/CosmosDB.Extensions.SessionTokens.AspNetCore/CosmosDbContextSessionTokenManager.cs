using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace CosmosDB.Extensions.SessionTokens.AspNetCore;

public class CosmosDbContextSessionTokenManager : ICosmosDbContextSessionTokenManager<HttpContext>
{
    private readonly ConditionalWeakTable<HttpContext, ConcurrentDictionary<uint, string>>
        _httpContextToContainerCodeSessionTokenDictionary = new();

    private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    public string? GetSessionTokenForContextFullyQualifiedContainer(
        HttpContext context, 
        Uri accountEndpoint, 
        string databaseName,
        string containerName)
    {
        string? result = null;

        if (_httpContextToContainerCodeSessionTokenDictionary.TryGetValue(context, out var databaseSessionTokenMap))
        {
            databaseSessionTokenMap.TryGetValue(CalculateContainerCode(accountEndpoint, databaseName, containerName), out result);
        }

        return result;
    }

    public void SetSessionTokenForContextAndFullyQualifiedContainer(
        HttpContext context, 
        Uri accountEndpoint, 
        string databaseName,
        string containerName, 
        string? sessionToken)
    {
        if (sessionToken == null)
        {
            return;
        }

        _httpContextToContainerCodeSessionTokenDictionary
            .GetOrCreateValue(context)
            .AddOrUpdate(CalculateContainerCode(accountEndpoint, databaseName, containerName), sessionToken, (_, _) => sessionToken);
    }

    public void SetSessionTokensForContext(HttpContext context,
        ConcurrentDictionary<uint, string> containerCodeToSessionTokens)
    {
        _httpContextToContainerCodeSessionTokenDictionary.Add(context, containerCodeToSessionTokens);
    }

    public bool TryGetSessionTokensForHttpContext(HttpContext context,
        [NotNullWhen(true)] out ConcurrentDictionary<uint, string>? containerCodeToSessionTokens)
    {
        return _httpContextToContainerCodeSessionTokenDictionary.TryGetValue(context, out containerCodeToSessionTokens);
    }

    private uint CalculateContainerCode(Uri accountEndpoint, string databaseName, string containerName)
    {
        // Uri.toString() adds a trailing slash automatically.
        var key = $"{accountEndpoint}{databaseName}/{containerName}";

        return _cache.GetOrCreate(key, entry =>
        {
            using var hash = SHA256.Create();
            var sha256Bytes = hash.ComputeHash(Encoding.UTF8.GetBytes((string)entry.Key));
            return BitConverter.ToUInt32(sha256Bytes, 0) % 1_000_000;
        });
    }
}