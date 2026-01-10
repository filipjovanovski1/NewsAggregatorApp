using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Ingestion;

namespace NewsApplication.Web.Controllers;

//[Authorize(Roles=…)]
[ApiController]
[Route("dev")]
public sealed class DevController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public DevController(ApplicationDbContext db) { _db = db; }

    [HttpPost("import/countries")]
    public async Task<IActionResult> ImportCountries(
        [FromServices] CountryImporter importer,
        [FromServices] IWebHostEnvironment env,
        CancellationToken ct)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "List_of_countries_with_ISO3.csv");
        var (count, errs) = await importer.ImportAsync(path, ct);
        return Ok(new { Upserted = count, Errors = errs });
    }

    [HttpPost("import/cities")]
    public async Task<IActionResult> ImportCities(
        [FromServices] CityImporter importer,
        [FromServices] IWebHostEnvironment env,
        CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(env.ContentRootPath, "Data", "List_of_cities.csv");
            if (!System.IO.File.Exists(path)) return NotFound(new { File = path });

            var (count, errs) = await importer.ImportAsync(path, ct);
            return Ok(new { Inserted = count, Errors = errs });
        }
        catch (Exception ex)
        {
            return Problem(title: "Cities import failed", detail: ex.ToString(), statusCode: 500);
        }
    }

    [HttpPost("cache/cleanup")]
    public async Task<IActionResult> CleanupCache(
        [FromServices] IArticleRepository repo,
        CancellationToken ct)
    {
        var expired = await repo.DeleteExpiredCachesAsync(ct);                 // TTL cleanup on ArticleCaches :contentReference[oaicite:6]{index=6}
        var orphans = await repo.DeleteOrphanArticlesAsync(TimeSpan.FromDays(2), ct); // safe orphan removal window :contentReference[oaicite:7]{index=7}
        return Ok(new { ExpiredCachesDeleted = expired, OrphanArticlesDeleted = orphans });
    }

    [HttpGet("ping")] public IActionResult Ping() => Ok(new { ok = true, time = DateTime.UtcNow });

    [HttpGet("stats")]
    public IActionResult Stats() => Ok(new { Countries = _db.Countries.Count(), Cities = _db.Cities.Count() });

    [HttpGet("dbinfo")]
    public async Task<IActionResult> DbInfo()
    {
        var row = await _db.Database.SqlQueryRaw<DbInfo>(@"
            select current_database() as ""Db"", current_user as ""Usr"",
                   inet_server_addr()::text as ""Host"", inet_server_port() as ""Port"",
                   current_schema() as ""Schema"", current_setting('search_path') as ""Path""
        ").FirstAsync();

        var expired = await _db.ArticleCaches.CountAsync(c => c.ExpiresAt < DateTimeOffset.UtcNow);
        return Ok(new { row.Db, row.Usr, row.Host, row.Port, row.Schema, row.Path, expired });
    }

    [HttpGet("dbclock")]
    public async Task<IActionResult> DbClock()
    {
        var serverNow = (await _db.Database.SqlQueryRaw<ScalarTimestamp>(@"select now() as ""Value""").FirstAsync()).Value!.Value;
        var appUtcNow = DateTimeOffset.UtcNow;

        var expiredByServer = (await _db.Database.SqlQueryRaw<ScalarInt>(@"
            select count(*) as ""Value"" from public.""ArticleCaches"" where ""ExpiresAt"" < now()").FirstAsync()).Value;

        var expiredByApp = await _db.ArticleCaches.CountAsync(c => c.ExpiresAt < appUtcNow);

        var minExp = (await _db.Database.SqlQueryRaw<ScalarTimestamp>(@"select min(""ExpiresAt"") as ""Value"" from public.""ArticleCaches""").FirstAsync()).Value;
        var maxExp = (await _db.Database.SqlQueryRaw<ScalarTimestamp>(@"select max(""ExpiresAt"") as ""Value"" from public.""ArticleCaches""").FirstAsync()).Value;

        return Ok(new { serverNow, appUtcNow, skewMinutes = (serverNow - appUtcNow).TotalMinutes, expiredByServer, expiredByApp, minExpiresAt = minExp, maxExpiresAt = maxExp });
    }

  
}
