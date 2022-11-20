using Castle.DynamicProxy;
using CosmosDB.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Interceptors;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Middleware;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<IProxyGenerator>(new ProxyGenerator());
builder.Services.AddSingleton<CosmosDbClientInterceptor<HttpContext>>();
builder.Services.AddSingleton<CosmosDbContextSessionTokenManager>();

builder.Services.AddSingleton<GetCurrentContextDelegate<HttpContext>>(provider =>
{
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    return () => httpContextAccessor.HttpContext;
});
builder.Services.AddSingleton<ICosmosDbContextSessionTokenManager<HttpContext>>(provider => provider.GetRequiredService<CosmosDbContextSessionTokenManager>());

builder.Services.AddSingleton(provider =>
{
    CosmosClient client = new(builder.Configuration["CosmosDB:PrimaryConnectionString"]);

    return provider.GetRequiredService<IProxyGenerator>()
        .CreateClassProxyWithTarget(client, provider.GetRequiredService<CosmosDbClientInterceptor<HttpContext>>());
});

builder.Services.AddHttpLogging(logging =>
{
    logging.RequestHeaders.Add("Cookie");
    logging.ResponseHeaders.Add("Set-Cookie");
});

var app = builder.Build();

app.UseMiddleware<CosmosDbSessionTokenCookiesHttpMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHttpLogging();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

namespace CosmosDB.Extensions.SessionTokens.AspNetCore.IntegrationTestsWebAPI
{
    public partial class Program
    {
    }
}
