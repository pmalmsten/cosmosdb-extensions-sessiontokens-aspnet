using Castle.DynamicProxy;
using CosmosDb.Extensions.SessionTokens.AspNetCore;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();
builder.Services.AddHttpContextAccessor();

builder.Services.AddSingleton<ProxyGenerator>();
builder.Services.AddSingleton<CosmosClientInterceptor<HttpContext>>();
builder.Services.AddSingleton<HttpContextContextAccessor>();
builder.Services.AddSingleton<CosmosDbContextSessionTokenManager>();

builder.Services.AddSingleton<IContextAccessor<HttpContext>>(provider => provider.GetRequiredService<HttpContextContextAccessor>());
builder.Services.AddSingleton<ICosmosDbContextSessionTokenManager<HttpContext>>(provider => provider.GetRequiredService<CosmosDbContextSessionTokenManager>());

builder.Services.AddSingleton<CosmosClient>(provider =>
{
    CosmosClient client =
        new("***REMOVED***");

    return provider.GetRequiredService<ProxyGenerator>()
        .CreateClassProxyWithTarget(client, provider.GetRequiredService<CosmosClientInterceptor<HttpContext>>());
});

builder.Services.AddHttpLogging(logging =>
{
    logging.RequestHeaders.Add("Cookie");
    logging.ResponseHeaders.Add("Set-Cookie");
});

var app = builder.Build();

app.UseMiddleware<CookieCosmosDbHttpMiddleware>();

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

public partial class Program
{
}
