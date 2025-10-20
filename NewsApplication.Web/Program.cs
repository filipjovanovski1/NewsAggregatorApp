using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Importers;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<CountryImporter>();
builder.Services.AddScoped<CityImporter>();

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
    var path = Path.Combine(repoData, "List_of_countries.csv");
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

app.Run();
