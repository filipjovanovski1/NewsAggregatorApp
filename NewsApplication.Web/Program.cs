using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Implementations;
using NewsApplication.Repository.Db.Implementations.Discovery;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Repository.Db.Interfaces.Discovery;
using NewsApplication.Service.Implementations;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Implementations.Ingestion;
using NewsApplication.Service.Implementations.Discovery;
using NewsApplication.Service.Implementations.Discovery.Workers;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Discovery;
using NewsApplication.Service.Interfaces.Ingestion;
using Npgsql;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var corsOrigins = new List<string>
{
    "http://localhost:5173",
    "https://localhost:5173"
};

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
corsOrigins.AddRange(configuredOrigins.Where(o => !string.IsNullOrWhiteSpace(o)));


builder.Services.AddCors(opts =>
{
    opts.AddPolicy("Client", p => p
        .WithOrigins(corsOrigins.Distinct().ToArray())
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var connStr = builder.Configuration.GetConnectionString("Default")!;

// Build a data source with dynamic JSON enabled
var dsb = new NpgsqlDataSourceBuilder(connStr);
dsb.EnableDynamicJson();                  // <-- Npgsql 8 setting
var dataSource = dsb.Build();

var migrationsAssembly = typeof(ApplicationDbContext).Assembly.FullName;
// Services
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(dataSource, npg => npg.MigrationsAssembly(migrationsAssembly)),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<ApplicationDbContext>(opt =>
    opt.UseNpgsql(dataSource, npg => npg.MigrationsAssembly(migrationsAssembly)));

builder.Services.AddScoped<CountryImporter>();
builder.Services.AddScoped<CityImporter>();

// Repositories (EF Core / data access)
builder.Services.AddScoped<ICityReadRepository, CityReadRepository>();
builder.Services.AddScoped<ICountryReadRepository, CountryReadRepository>();
builder.Services.AddScoped<IArticleRepository, ArticleRepository>();
builder.Services.AddScoped<IDiscoveryTargetRepository, DiscoveryTargetRepository>();
builder.Services.AddScoped<IDiscoveryJobRepository, DiscoveryJobRepository>();
builder.Services.AddScoped<INewsSourceRepository, NewsSourceRepository>();

// Application services (orchestration)
builder.Services.AddScoped<ICityReadService, CityReadService>();
builder.Services.AddScoped<ICountryReadService, CountryReadService>();
builder.Services.AddSingleton<IQueryTokenizer, QueryTokenizer>();

// Scope Preview
builder.Services.AddScoped<IScopePolicy, ScopePolicy>();
builder.Services.AddScoped<IScopeResolverService, ScopeResolverService>();

builder.Services.Configure<NewsdataOptions>(builder.Configuration.GetSection("Newsdata"));
builder.Services.AddHttpClient<INewsdataClient, NewsdataClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsdataOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl); // full endpoint
});
builder.Services.AddHttpClient("nominatim", http =>
{
    http.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsAggregatorApp/1.0 (+https://newsaggregatorapp.local/contact)");
});
builder.Services.AddScoped<IArticleIngestionService, ArticleIngestionService>();

builder.Services.Configure<DiscoveryPipelineOptions>(
    builder.Configuration.GetSection("DiscoveryPipeline"));
builder.Services.AddHttpClient<IDiscoveryPipelineClient, DiscoveryPipelineClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscoveryPipelineOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddScoped<IDiscoveryJobService, DiscoveryJobService>();
builder.Services.AddScoped<IDiscoveryResultImportService, DiscoveryResultImportService>();
builder.Services.Configure<DiscoverySchedulerOptions>(
    builder.Configuration.GetSection("DiscoveryScheduler"));
builder.Services.AddHttpClient("rss", http =>
{
    http.Timeout = TimeSpan.FromSeconds(45);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsAggregatorApp/1.0 RSS Poller");
});
builder.Services.AddHostedService<DiscoveryDispatcherWorker>();
builder.Services.AddHostedService<StaleDiscoveryJobWorker>();
builder.Services.AddHostedService<FeedRevalidationWorker>();
builder.Services.AddHostedService<RssPollingWorker>();

builder.Services.AddControllers(); 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("Client");

var applyMigrations = builder.Configuration.GetValue<bool?>("Database:ApplyMigrationsOnStartup") ?? true;

// dev: auto-apply migrations (keep your block)
if (applyMigrations)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    db.Database.Migrate();
}
// add before app.Run();
app.MapGet("/_routes", (IEnumerable<EndpointDataSource> sources) =>
{
    var routes = sources
        .SelectMany(s => s.Endpoints)
        .OfType<RouteEndpoint>()
        .Select(e => new
        {
            Pattern = e.RoutePattern.RawText,
            Display = e.DisplayName
        });
    return Results.Ok(routes);
});



var devEnabled = builder.Configuration.GetValue<bool>("Dev:Enabled", builder.Environment.IsDevelopment());
var devToken = builder.Configuration["Dev:AdminToken"]; // from env/secrets

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/dev", StringComparison.OrdinalIgnoreCase))
    {
        // If disabled -> pretend it doesn't exist
        if (!devEnabled)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Require token (you can also require it in Development if you want)
        if (string.IsNullOrWhiteSpace(devToken))
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsync("Dev admin token not configured.");
            return;
        }

        // Read header
        if (!ctx.Request.Headers.TryGetValue("X-Dev-Token", out var provided))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Constant-time compare (prevents timing leaks)
        if (!FixedTimeEquals(provided.ToString(), devToken))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }

    await next();
});

static bool FixedTimeEquals(string a, string b)
{
    var ba = Encoding.UTF8.GetBytes(a);
    var bb = Encoding.UTF8.GetBytes(b);
    return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
}


app.MapControllers();
app.Run();

// Exposes the top-level entry point to WebApplicationFactory integration tests.
public partial class Program;
