using Microsoft.AspNetCore.Mvc;
using NewsApplication.Domain.Helpers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces.Ingestion;
using NewsApplication.Web.Summarization;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("articles")]
public sealed class ArticlesController : ControllerBase
{
    // IMPORTANT: Each provider page = max 10 articles (fixed by your upstream).
    private const int ProviderPageSize = 10;
    private const int StreamBatchSize = 6;

    /// <summary>
    /// Returns one transport batch for the continuous article stream. The client appends
    /// each batch instead of replacing the currently visible carousel articles.
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string scopeKey,
        [FromQuery] int uiPage, // 1-based from the UI overlay
        [FromQuery] string? summaryLanguage,
        [FromServices] IArticleRepository repo,
        [FromServices] IArticleIngestionService ingest,
        [FromServices] IArticleSummaryCoordinator summaries,
        CancellationToken ct)
    {
        //MODIFIED
        var flow = Request.Headers["X-SearchBar-Flow"].ToString();
        if (!string.IsNullOrWhiteSpace(flow))
            Console.WriteLine($"SB flow (articles/search): {flow}");
        //MODIFIED
        if (uiPage < 1) uiPage = 1;

        if (!SummaryLanguage.TryNormalize(summaryLanguage ?? "mk", out var language))
        {
            return BadRequest(new
            {
                error = "Unsupported summary language.",
                supportedLanguages = SummaryLanguage.Codes
            });
        }

        // 0) Detect "brand new scope" → preload provider pages 1 and 2 (each 10 items).
        //    If page 1 is fresh, GetOrFetchPageAsync returns cache without hitting the provider.
        //    TTL is 10 minutes on new fetches.
        //    (HasFreshPageAsync short-circuits the API on fresh cache.) :contentReference[oaicite:0]{index=0}
        if (!await repo.HasFreshPageAsync(scopeKey, page: 1, now: DateTimeOffset.UtcNow, ct))
        {
            await ingest.GetOrFetchPageAsync(scopeKey, page: 1, pageSize: ProviderPageSize, ct);  // warm #1 :contentReference[oaicite:1]{index=1}
            await ingest.GetOrFetchPageAsync(scopeKey, page: 2, pageSize: ProviderPageSize, ct);  // warm #2 now
        }

        // 1) Make sure we have enough distinct items for the requested stream batch.
        //    If not, keep fetching *additional* provider pages until we cover it or the provider exhausts.
        var offset = (uiPage - 1) * StreamBatchSize;
        var need = offset + StreamBatchSize;
        var have = await repo.CountDistinctForScopeAsync(scopeKey, ct);                            // :contentReference[oaicite:2]{index=2}

        while (have < need)
        {
            var nextProviderPage = await repo.GetHighestCachedPageAsync(scopeKey, ct) + 1;
            await ingest.FetchAndCachePageAsync(scopeKey, nextProviderPage, ProviderPageSize, ct);   // writes cache + sets 10m TTL :contentReference[oaicite:3]{index=3}

            var h2 = await repo.CountDistinctForScopeAsync(scopeKey, ct);
            if (h2 <= have) break; // no progress → provider likely exhausted (or heavy de-dupe)
            have = h2;
        }

        // 2) Read a flat feed ordered by (Page ASC, Position ASC, Published DESC), then distinct-by-id, then slice. :contentReference[oaicite:4]{index=4}
        var upTo = Math.Max(need, have) + ProviderPageSize;
        var flat = await repo.GetFlatFeedAsync(scopeKey, upTo, ct);                                  // :contentReference[oaicite:5]{index=5}

        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderedIds = new List<string>();
        foreach (var f in flat)
        {
            var normalizedTitle = TitleNormalizer.Normalize(f.Title);
            if (!seenTitles.Add(normalizedTitle))
                continue;

            orderedIds.Add(f.ArticleId);
        }
        var total = orderedIds.Count;
        var slice = orderedIds.Skip(offset).Take(StreamBatchSize).ToList();
        var rows = await repo.LoadArticlesByIdsAsync(slice, ct);                                  // :contentReference[oaicite:6]{index=6}
        var rowsById = rows.ToDictionary(a => a.ArticleId, StringComparer.Ordinal);
        var items = slice.Select(id =>
        {
            var article = rowsById[id];
            var summary = summaries.GetOrQueue(article, language);
            return new
            {
                articleId = article.ArticleId,
                provider = article.Provider,
                title = article.Title,
                description = article.Description,
                imageUrl = article.ImageUrl,
                publisher = article.Publisher,
                url = article.Url,
                publishedTime = article.PublishedTime,
                categories = article.Categories,
                translatedTitle = summary.TranslatedTitle,
                summary = summary.Summary,
                summaryLanguage = summary.Language,
                summaryStatus = summary.Status
            };
        }).ToList();

        var hasNewer = uiPage > 1;
        var hasOlder = total > (offset + StreamBatchSize);
        var nextUiPage = hasOlder ? uiPage + 1 : uiPage;

        // 3) Hint the client which provider page to prewarm next (keep one ahead).
        //    Rule: when user is on UI page N, warm provider page (floor(distinct/10)+1) or simply N+1 at minimum.
        var distinctSoFar = Math.Min(total, need);
        var minProviderToWarm = Math.Max(1, (distinctSoFar / ProviderPageSize) + 1);

        return Ok(new
        {
            scopeKey,
            uiPage,
            pageSize = StreamBatchSize,
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

    [HttpGet("summaries")]
    public async Task<IActionResult> GetSummaries(
        [FromQuery] string[] articleIds,
        [FromQuery] string? summaryLanguage,
        [FromServices] IArticleRepository repo,
        [FromServices] IArticleSummaryCoordinator summaries,
        CancellationToken ct)
    {
        if (!SummaryLanguage.TryNormalize(summaryLanguage ?? "mk", out var language))
        {
            return BadRequest(new
            {
                error = "Unsupported summary language.",
                supportedLanguages = SummaryLanguage.Codes
            });
        }

        var ids = articleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToArray();
        if (ids.Length == 0) return Ok(new { items = Array.Empty<object>() });

        var rows = await repo.LoadArticlesByIdsAsync(ids, ct);
        var rowsById = rows.ToDictionary(a => a.ArticleId, StringComparer.Ordinal);
        var items = ids
            .Where(rowsById.ContainsKey)
            .Select(id => summaries.GetOrQueue(rowsById[id], language))
            .Select(summary => new
            {
                articleId = summary.ArticleId,
                translatedTitle = summary.TranslatedTitle,
                summary = summary.Summary,
                summaryLanguage = summary.Language,
                summaryStatus = summary.Status
            })
            .ToList();

        return Ok(new { items });
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
