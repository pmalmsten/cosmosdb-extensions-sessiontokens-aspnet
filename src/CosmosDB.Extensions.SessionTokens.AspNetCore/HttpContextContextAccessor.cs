using Microsoft.AspNetCore.Http;

namespace CosmosDb.Extensions.SessionTokens.AspNetCore;

public class HttpContextContextAccessor : IContextAccessor<HttpContext>
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextContextAccessor(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public HttpContext? CurrentContext => _accessor.HttpContext;
}