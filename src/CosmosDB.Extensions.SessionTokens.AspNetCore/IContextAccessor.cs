namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public interface IContextAccessor<out T>
{
    public T? CurrentContext { get; }
}