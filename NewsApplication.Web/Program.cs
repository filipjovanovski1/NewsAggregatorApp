using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Implementations;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Implementations;
using NewsApplication.Service.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")),
     contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<ApplicationDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default"),
                  npg => npg.CommandTimeout(5))); // short timeout helps under load

builder.Services.AddScoped<CountryImporter>();
builder.Services.AddScoped<CityImporter>();

// Repositories (EF Core / data access)
builder.Services.AddScoped<ICityReadRepository, CityReadRepository>();
builder.Services.AddScoped<ICountryReadRepository, CountryReadRepository>();

// Application services (orchestration)
builder.Services.AddScoped<ICityReadService, CityReadService>();
builder.Services.AddScoped<ICountryReadService, CountryReadService>();

builder.Services.AddSingleton<IQueryTokenizer, QueryTokenizer>();

// Scope Preview
builder.Services.AddScoped<IScopePolicy, ScopePolicy>();
builder.Services.AddScoped<IScopeResolverService, ScopeResolverService>();
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

app.Run();
