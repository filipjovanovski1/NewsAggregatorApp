using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Implementations;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Implementations;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Implementations.Ingestion;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Ingestion;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddScoped<IArticleIngestionService, ArticleIngestionService>();
var app = builder.Build();

// (Dev) auto-apply migrations
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// (Dev) endpoints to import + check counts
app.MapPost("/dev/import/countries", async (CountryImporter importer, IWebHostEnvironment env, CancellationToken ct) =>
{
    var repoData = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "NewsApplication.Repository", "Data"));
    var path = Path.Combine(repoData, "List_of_countries_with_ISO3.csv");
    var (count, errs) = await importer.ImportAsync(path, ct);
    return Results.Ok(new { Upserted = count, Errors = errs });
});

app.MapPost("/dev/import/cities", async (CityImporter importer, IWebHostEnvironment env, CancellationToken ct) =>
{
    try
    {
        var repoData = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "NewsApplication.Repository", "Data"));
        var path = Path.Combine(repoData, "List_of_cities.csv");

        if (!System.IO.File.Exists(path))
            return Results.NotFound(new { File = path });

        var (count, errs) = await importer.ImportAsync(path, ct);
        return Results.Ok(new { Inserted = count, Errors = errs });
    }
    catch (Exception ex)
    {
        // Return full details in dev so you can see the problem immediately
        return Results.Problem(
            title: "Cities import failed",
            detail: ex.ToString(),
            statusCode: 500);
    }
});
//TEST
app.MapGet("/dev/ping", () => Results.Ok(new { ok = true, time = DateTime.UtcNow }));

app.MapGet("/dev/stats", (ApplicationDbContext db) =>
    Results.Ok(new
    {
        Countries = db.Countries.Count(),
        Cities = db.Cities.Count()
    }));

// 🔍 Test: City search
app.MapGet("/dev/search/city", async (
    string q,
    ICityReadService svc,
    CancellationToken ct) =>
{
    var results = await svc.SearchAsync(q, 10, ct);
    return Results.Ok(results);
});

// 🔍 Test: Country search
app.MapGet("/dev/search/country", async (
    string q,
    ICountryReadService svc,
    CancellationToken ct) =>
{
    var results = await svc.SearchAsync(q, 10, ct);
    return Results.Ok(results);
});

app.MapGet("/dev/preview", async (
    string q,
    IScopeResolverService svc,
    CancellationToken ct) =>
{
    var preview = await svc.PreviewAsync(q, ct);
    return Results.Ok(preview);
});

app.MapPost("/dev/cache/fetch", async (
    string scopeKey, int page, int pageSize,
    IArticleIngestionService svc, CancellationToken ct) =>
{
    var cache = await svc.FetchAndCachePageAsync(scopeKey, page, pageSize, ct);
    return Results.Ok(new { cache.Id, cache.ScopeKey, cache.Page, cache.NextPageToken, cache.ExpiresAt });
});

// Get a cached page
app.MapGet("/dev/cache/page", async (
    string scopeKey,
    int page,
    IArticleRepository repo,
    CancellationToken ct) =>
{
    var cache = await repo.GetPageAsync(scopeKey, page, ct);
    if (cache is null) return Results.NotFound();

    var dto = new ArticleCachePageDto(
        cache.Id,
        cache.ScopeKey,
        cache.Page,
        cache.NextPageToken,
        cache.ExpiresAt,
        cache.Items
            .OrderBy(i => i.Position ?? int.MaxValue)
            .Select(i => new ArticleCacheItemDto(
                i.ArticleId,
                i.Position,
                new ArticleDto(
                    i.Article.ArticleId,
                    i.Article.Provider,
                    i.Article.Title,
                    i.Article.Description,
                    i.Article.ImageUrl,
                    i.Article.Publisher,
                    i.Article.Url,
                    i.Article.PublishedTime,
                    i.Article.Categories
                )))
            .ToList()
    );

    return Results.Ok(dto);
});

// Run cleanup (expired + orphan)
app.MapPost("/dev/cache/cleanup", async (IArticleRepository repo) =>
{
    var expired = await repo.DeleteExpiredCachesAsync(DateTimeOffset.UtcNow);
    var orphans = await repo.DeleteOrphanArticlesAsync(DateTimeOffset.UtcNow.AddDays(-2));
    return Results.Ok(new
    {
        ExpiredCachesDeleted = expired,
        OrphanArticlesDeleted = orphans
    });
});


app.Run();
