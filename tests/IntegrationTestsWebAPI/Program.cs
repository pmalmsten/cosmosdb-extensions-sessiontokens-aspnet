using CosmosDB.Extensions.SessionTokens.AspNetCore;
using CosmosDB.Extensions.SessionTokens.AspNetCore.Middleware;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/app/secrets/appsettings.secrets.json", optional: true);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddLogging();

builder.Services.AddCosmosDbSessionTokenTracingServices();

builder.Services.AddSingleton(provider => 
    new CosmosClient(builder.Configuration["CosmosDB:PrimaryConnectionString"], new CosmosClientOptions()
        {
            ApplicationRegion = Regions.WestUS
        })
        .WithSessionTokenTracing(provider));

builder.Services.AddHttpLogging(logging =>
{
    logging.RequestHeaders.Add("Cookie");
    logging.ResponseHeaders.Add("Set-Cookie");
});

var app = builder.Build();

app.UseMiddleware<CosmosDbSessionTokenCookiesMiddleware>();

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