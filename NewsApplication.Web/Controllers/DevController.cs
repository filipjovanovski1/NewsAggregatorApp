using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Ingestion;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Service.Interfaces.Discovery;

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
     [FromServices] IConfiguration config,
     CancellationToken ct)
    {
        var dataRoot =
            config["Dev:DataRoot"]
            ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "NewsApplication.Repository", "Data"));

        var path = Path.Combine(dataRoot, "List_of_countries_with_ISO3.csv");
        if (!System.IO.File.Exists(path)) return NotFound(new { File = path });

        var (count, errs) = await importer.ImportAsync(path, ct);
        return Ok(new { Upserted = count, Errors = errs, DataRoot = dataRoot });
    }

    [HttpPost("import/cities")]
    public async Task<IActionResult> ImportCities(
        [FromServices] CityImporter importer,
        [FromServices] IWebHostEnvironment env,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        var dataRoot =
            config["Dev:DataRoot"]
            ?? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "NewsApplication.Repository", "Data"));

        var path = Path.Combine(dataRoot, "List_of_cities.csv");
        if (!System.IO.File.Exists(path)) return NotFound(new { File = path });

        var (count, errs) = await importer.ImportAsync(path, ct);
        return Ok(new { Inserted = count, Errors = errs, DataRoot = dataRoot });
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

    [HttpGet("discovery/targets")]
    public async Task<IActionResult> DiscoveryTargets(CancellationToken ct)
    {
        var targets = await _db.DiscoveryTargets
            .AsNoTracking()
            .Include(x => x.Country)
            .Include(x => x.City)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.NextDueAt)
            .Select(x => new
            {
                x.Id,
                x.CountryIso2,
                Country = x.Country!.Name,
                x.CityId,
                City = x.City != null ? x.City.Name : null,
                x.Priority,
                x.CadenceDays,
                x.NextDueAt,
                x.LastSuccessAt,
                x.ConsecutiveFailures,
                x.ConsecutiveEmptyRuns,
                x.IsEnabled
            })
            .ToListAsync(ct);
        return Ok(targets);
    }

    [HttpPost("discovery/targets")]
    public async Task<IActionResult> CreateDiscoveryTarget(
        CreateDiscoveryTargetRequest request,
        CancellationToken ct)
    {
        var iso2 = request.CountryIso2.Trim().ToUpperInvariant();
        if (request.CadenceDays is not (30 or 90 or 180))
            return BadRequest(new { error = "cadence_days must be 30, 90, or 180" });
        if (!await _db.Countries.AnyAsync(x => x.Iso2 == iso2, ct))
            return BadRequest(new { error = "unknown country" });
        if (request.CityId is { } cityId &&
            !await _db.Cities.AnyAsync(x => x.Id == cityId && x.CountryIso2 == iso2, ct))
            return BadRequest(new { error = "city does not belong to country" });

        var exists = await _db.DiscoveryTargets.AnyAsync(
            x => x.CountryIso2 == iso2 && x.CityId == request.CityId, ct);
        if (exists)
            return Conflict(new { error = "target already exists" });

        var target = new DiscoveryTarget
        {
            CountryIso2 = iso2,
            CityId = request.CityId,
            Priority = request.Priority,
            CadenceDays = request.CadenceDays,
            NextDueAt = DateTimeOffset.UtcNow,
            IsEnabled = true
        };
        _db.DiscoveryTargets.Add(target);
        await _db.SaveChangesAsync(ct);
        return Created($"/dev/discovery/targets/{target.Id}", new { target.Id });
    }

    [HttpPatch("discovery/targets/{id:guid}/enabled")]
    public async Task<IActionResult> SetDiscoveryTargetEnabled(
        Guid id,
        SetDiscoveryTargetEnabledRequest request,
        CancellationToken ct)
    {
        var target = await _db.DiscoveryTargets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (target is null)
            return NotFound();
        target.IsEnabled = request.IsEnabled;
        if (request.IsEnabled && target.NextDueAt < DateTimeOffset.UtcNow)
            target.NextDueAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { target.Id, target.IsEnabled, target.NextDueAt });
    }

    [HttpPost("discovery/targets/{id:guid}/dispatch")]
    public async Task<IActionResult> DispatchDiscoveryTarget(
        Guid id,
        [FromServices] IDiscoveryJobService jobs,
        CancellationToken ct)
    {
        var target = await _db.DiscoveryTargets
            .Include(x => x.Country)
            .Include(x => x.City)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (target is null)
            return NotFound();
        if (!target.IsEnabled)
            return Conflict(new { error = "target is disabled" });

        var result = await jobs.StartAsync(target, ct);
        return Ok(result);
    }

  
}
