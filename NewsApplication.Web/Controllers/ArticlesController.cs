using Microsoft.AspNetCore.Mvc;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces.Ingestion;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("articles")]
public sealed class ArticlesController : ControllerBase
{
    // IMPORTANT: Each provider page = max 10 articles (fixed by your upstream).
    private const int ProviderPageSize = 10;
    private const int UiPageSize = 6; // your overlay shows 6 at a time

    /// <summary>
    /// Returns a 6-article slice for the given scope and UI page, and on a *new scope*
    /// preloads provider pages 1 AND 2 immediately on the server.
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string scopeKey,
        [FromQuery] int uiPage, // 1-based from the UI overlay
        [FromServices] IArticleRepository repo,
        [FromServices] IArticleIngestionService ingest,
        CancellationToken ct)
    {
        if (uiPage < 1) uiPage = 1;

        // 0) Detect "brand new scope" → preload provider pages 1 and 2 (each 10 items).
        //    If page 1 is fresh, GetOrFetchPageAsync returns cache without hitting the provider.
        //    TTL is 10 minutes on new fetches.
        //    (HasFreshPageAsync short-circuits the API on fresh cache.) :contentReference[oaicite:0]{index=0}
        if (!await repo.HasFreshPageAsync(scopeKey, page: 1, now: DateTimeOffset.UtcNow, ct))
        {
            await ingest.GetOrFetchPageAsync(scopeKey, page: 1, pageSize: ProviderPageSize, ct);  // warm #1 :contentReference[oaicite:1]{index=1}
            await ingest.GetOrFetchPageAsync(scopeKey, page: 2, pageSize: ProviderPageSize, ct);  // warm #2 now
        }

        // 1) Make sure we have enough distinct items for the requested UI window [offset, offset+UiPageSize)
        //    If not, keep fetching *additional* provider pages until we cover it or the provider exhausts.
        var offset = (uiPage - 1) * UiPageSize;
        var need = offset + UiPageSize;
        var have = await repo.CountDistinctForScopeAsync(scopeKey, ct);                            // :contentReference[oaicite:2]{index=2}

        while (have < need)
        {
            // Heuristic: every provider page yields up to 10 items.
            var nextProviderPage = Math.Max(1, (have / ProviderPageSize) + 1);
            await ingest.FetchAndCachePageAsync(scopeKey, nextProviderPage, ProviderPageSize, ct);   // writes cache + sets 10m TTL :contentReference[oaicite:3]{index=3}

            var h2 = await repo.CountDistinctForScopeAsync(scopeKey, ct);
            if (h2 <= have) break; // no progress → provider likely exhausted (or heavy de-dupe)
            have = h2;
        }

        // 2) Read a flat feed ordered by (Page ASC, Position ASC, Published DESC), then distinct-by-id, then slice. :contentReference[oaicite:4]{index=4}
        var upTo = Math.Max(need, have);
        var flat = await repo.GetFlatFeedAsync(scopeKey, upTo, ct);                                  // :contentReference[oaicite:5]{index=5}

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var orderedIds = new List<string>();
        foreach (var f in flat)
            if (seen.Add(f.ArticleId)) orderedIds.Add(f.ArticleId);

        var total = orderedIds.Count;
        var slice = orderedIds.Skip(offset).Take(UiPageSize).ToList();
        var rows = await repo.LoadArticlesByIdsAsync(slice, ct);                                  // :contentReference[oaicite:6]{index=6}
        var items = slice.Select(id => rows.First(a => a.ArticleId == id)).Select(a => new {
            articleId = a.ArticleId,
            provider = a.Provider,
            title = a.Title,
            description = a.Description,
            imageUrl = a.ImageUrl,
            publisher = a.Publisher,
            url = a.Url,
            publishedTime = a.PublishedTime,
            categories = a.Categories
        });

        var hasNewer = uiPage > 1;
        var hasOlder = total > (offset + UiPageSize);
        var nextUiPage = hasOlder ? uiPage + 1 : uiPage;

        // 3) Hint the client which provider page to prewarm next (keep one ahead).
        //    Rule: when user is on UI page N, warm provider page (floor(distinct/10)+1) or simply N+1 at minimum.
        var distinctSoFar = Math.Min(total, need);
        var minProviderToWarm = Math.Max(1, (distinctSoFar / ProviderPageSize) + 1);

        return Ok(new
        {
            scopeKey,
            uiPage,
            pageSize = UiPageSize,
            hasNewer,
            hasOlder,
            totalDistinct = total,
            nextUiPage,
            prefetch = new
            {
                providerPage = minProviderToWarm,
                providerPageSize = ProviderPageSize
            },
            items
        });
    }

    /// <summary>
    /// Explicit pre-warm of a provider page (always 10 upstream). Ignores pageSize param on purpose.
    /// </summary>
    [HttpPost("cache/fetch")]
    public async Task<IActionResult> FetchCachePage(
        [FromQuery] string scopeKey,
        [FromQuery] int page,
        [FromServices] IArticleIngestionService ingest,
        CancellationToken ct)
    {
        // Short-circuits if page is already fresh; otherwise fetches and sets ExpiresAt = now + 10m. :contentReference[oaicite:7]{index=7}
        var cache = await ingest.GetOrFetchPageAsync(scopeKey, page, pageSize: ProviderPageSize, ct);
        return Ok(new { cache.Id, cache.ScopeKey, cache.Page, cache.NextPageToken, cache.ExpiresAt });
    }
}
