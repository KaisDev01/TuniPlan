using System.Text.Json.Serialization;
using AIL;
using BL;
using DAL;
using DAL.Context;
using DAL.Seed;
using DAO;
using IAConnectors;
using LoggerService;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using NotificationService;
using OperationStorage;
using Scalar.AspNetCore;
using TuniPlan.Infrastructure;
using TuniPlan.Security;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Remove the "Server: Kestrel" header
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

// ---------------- Layers (n-tier)
builder.Services
    .AddLoggerService(config)            // LoggerService
    .AddDataAccessLayer(config)          // DAL  : DbContext (EF Core, SQL Server)
    .AddDataAccessObjects()              // DAO  : Repositories + Unit of Work
    .AddNotificationServices(config)     // NotificationService : SMS / WhatsApp / push
    .AddOperationStorage(config)         // OperationStorage : uploaded images
    .AddAiConnectors(config)             // IAConnectors : LLM provider
    .AddAiLayer()                        // AIL : secretary agent
    .AddBusinessLayer(config)            // BL   : managers
    .AddApiSecurity(config, builder.Environment);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

var app = builder.Build();

// ---------------- Database: migrate + demo data (development)
if (config.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TuniPlanDbContext>();
    if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync(); // before the first "dotnet ef migrations add"
    if (config.GetValue("Database:SeedDemoData", false))
        await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

// ---------------- HTTP pipeline
app.UseForwardedHeaders();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles(); // wwwroot/uploads (validated images only)
app.UseCors(ServiceCollectionExtensions.CorsPolicy);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || config.GetValue("OpenApi:Enabled", false))
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(o => o.WithTitle("TuniPlan API")).AllowAnonymous();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
