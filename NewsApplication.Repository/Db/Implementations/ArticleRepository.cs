using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.Cache;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.Helpers;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Interfaces;
using Npgsql;
using System.Data;
using System.Text;

namespace NewsApplication.Repository.Db.Implementations;

public sealed class ArticleRepository : IArticleRepository
{
    private readonly ApplicationDbContext _db;

    public ArticleRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    // -----------------------------
    //  UpsertAsync(IEnumerable<Article>)
    //  - Upsert by provider identity while retaining the backend-generated Id
    //  - Update mutable columns on conflict
    // -----------------------------
    public async Task<IReadOnlyList<Article>> UpsertAsync(
        IEnumerable<Article> articles,
        CancellationToken ct = default)
    {
        var incoming = articles.ToList();
        if (incoming.Count == 0)
            return [];

        var persisted = new List<Article>(incoming.Count);

        foreach (var article in incoming)
        {
            Article? existing = null;

            if (!string.IsNullOrWhiteSpace(article.ProviderArticleId))
            {
                existing = await _db.Articles.FirstOrDefaultAsync(
                    a => a.Provider == article.Provider &&
                         a.ProviderArticleId == article.ProviderArticleId,
                    ct);
            }

            // Fallback for providers such as RSS that may not supply an ID.
            if (existing is null && !string.IsNullOrWhiteSpace(article.Url))
            {
                existing = await _db.Articles.FirstOrDefaultAsync(
                    a => a.Provider == article.Provider &&
                         a.Url == article.Url,
                    ct);
            }

            if (existing is null)
            {
                _db.Articles.Add(article);
                persisted.Add(article);
                continue;
            }

            existing.Title = article.Title;
            existing.Description = article.Description;
            existing.ImageUrl = article.ImageUrl;
            existing.Publisher = article.Publisher;
            existing.Url = article.Url;
            existing.PublishedTime = article.PublishedTime;
            existing.Categories = article.Categories;

            persisted.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        return persisted;
    }

    // -----------------------------
    //  GetPageAsync(scopeKey, page)
    //  - Load ArticleCache + Items (+ Article)
    // -----------------------------
    public async Task<ArticleCache?> GetPageAsync(string scopeKey, int page, CancellationToken ct = default)
    {
        return await _db.ArticleCaches
       .Where(c => c.ScopeKey == scopeKey && c.Page == page && c.ExpiresAt > DateTimeOffset.UtcNow)
       .Include(c => c.Items).ThenInclude(i => i.Article)
       .FirstOrDefaultAsync(ct);

    }

    // -----------------------------
    //  PutPageAsync
    //  - Upsert ArticleCache by (ScopeKey, Page)
    //  - Idempotently insert ArticleCacheItem rows (ON CONFLICT DO NOTHING)
    // -----------------------------
    public async Task<ArticleCache> PutPageAsync(
        string scopeKey,
        int page,
        string? nextPageToken,
        DateTimeOffset expiresAt,
        IReadOnlyList<(Guid articleId, int? position)> items,
        CancellationToken ct = default)
    {
        // Always create a new versioned row
        // Overwrite the single row for (ScopeKey, Page)
        var cache = await _db.ArticleCaches
            .FirstOrDefaultAsync(c => c.ScopeKey == scopeKey && c.Page == page, ct);

        if (cache is null)
        {
            cache = new ArticleCache
            {
                ScopeKey = scopeKey,
                Page = page,
                NextPageToken = nextPageToken,
                ExpiresAt = expiresAt
            };
            _db.ArticleCaches.Add(cache);
            await _db.SaveChangesAsync(ct); // get Id
        }
        else
        {
            cache.NextPageToken = nextPageToken;
            cache.ExpiresAt = expiresAt;
            await _db.SaveChangesAsync(ct);
        }

        await _db.ArticleCacheItems
           .Where(i => i.ArticleCacheId == cache.Id)
           .ExecuteDeleteAsync(ct);

        if (items.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"INSERT INTO ""ArticleCacheItems"" (""ArticleCacheId"", ""ArticleId"", ""Position"") VALUES");
            var parameters = new List<Npgsql.NpgsqlParameter>();

            for (int i = 0; i < items.Count; i++)
            {
                var (articleId, pos) = items[i];
                if (i > 0) sb.AppendLine(",");
                sb.Append($"(@cid{i}, @aid{i}, @pos{i})");

                parameters.Add(new Npgsql.NpgsqlParameter($"cid{i}", cache.Id));
                parameters.Add(new Npgsql.NpgsqlParameter($"aid{i}", articleId));
                parameters.Add(new Npgsql.NpgsqlParameter($"pos{i}", (object?)pos ?? DBNull.Value));
            }

            sb.AppendLine(@" ON CONFLICT (""ArticleCacheId"", ""ArticleId"") DO NOTHING;");
            await _db.Database.ExecuteSqlRawAsync(sb.ToString(), parameters.ToArray(), ct);
        }

        return cache;
    }

    public async Task<bool> PruneDuplicateTitlesForCacheAsync(string scopeKey, int page, CancellationToken ct = default)
    {
        var cacheId = await _db.ArticleCaches
            .Where(c => c.ScopeKey == scopeKey && c.Page == page)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (cacheId is null)
            return false;

        var items = await (
            from i in _db.ArticleCacheItems
            where i.ArticleCacheId == cacheId
            join a in _db.Articles on i.ArticleId equals a.Id
            orderby i.Position ?? int.MaxValue, i.ArticleId
            select new { i.ArticleId, a.Title }
        ).ToListAsync(ct);

        if (items.Count <= 1)
            return false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toRemove = new List<Guid>();

        foreach (var item in items)
        {
            var normalized = TitleNormalizer.Normalize(item.Title);
            if (!seen.Add(normalized))
                toRemove.Add(item.ArticleId);
        }

        if (toRemove.Count == 0)
            return false;

        var distinctRemovals = toRemove.Distinct().ToArray();

        await _db.ArticleCacheItems
            .Where(i => i.ArticleCacheId == cacheId && distinctRemovals.Contains(i.ArticleId))
            .ExecuteDeleteAsync(ct);

        var stillLinked = await _db.ArticleCacheItems
            .Where(i => distinctRemovals.Contains(i.ArticleId))
            .Select(i => i.ArticleId)
            .Distinct()
            .ToListAsync(ct);

        var orphanIds = distinctRemovals.Except(stillLinked).ToArray();
        if (orphanIds.Length > 0)
        {
            await _db.Articles
                .Where(a => orphanIds.Contains(a.Id))
                .ExecuteDeleteAsync(ct);
        }

        return true;
    }

    // -----------------------------
    //  GetNextPageTokenForAsync(scopeKey, page)
    //  - Fetch token from (scopeKey, page-1)
    // -----------------------------
    public async Task<string?> GetNextPageTokenForAsync(string scopeKey, int page, CancellationToken ct = default)
    {
        if (page <= 1) return null;

        return await _db.ArticleCaches
            .Where(c => c.ScopeKey == scopeKey && c.Page == page - 1)
            .Select(c => c.NextPageToken)
            .FirstOrDefaultAsync(ct);
    }

    // -----------------------------
    //  DeleteExpiredCachesAsync(now)
    //  - TTL cleanup (CASCADE removes ArticleCacheItem links)
    // -----------------------------
    public async Task<int> DeleteExpiredCachesAsync(CancellationToken ct = default)
    {
        const string sql = @"DELETE FROM public.""ArticleCaches""
                         WHERE ""ExpiresAt"" < now();";
        return await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    // -----------------------------
    //  DeleteOrphanArticlesAsync(olderThan)
    //  - Remove Articles with no ArticleCacheItem references (with safety window)
    // -----------------------------
    public async Task<int> DeleteOrphanArticlesAsync(TimeSpan safetyWindow, CancellationToken ct = default)
    {
        // Npgsql maps TimeSpan -> INTERVAL
        return await _db.Database.ExecuteSqlInterpolatedAsync(
            $@"
        DELETE FROM public.""Articles"" a
        WHERE NOT EXISTS (
            SELECT 1 FROM public.""ArticleCacheItems"" i
            WHERE i.""ArticleId"" = a.""Id""
        )
        AND a.""InsertedAt"" < (now() - {safetyWindow});", ct);
    }
    // Count distinct article titles across all cached pages for a scope
    public async Task<int> CountDistinctForScopeAsync(string scopeKey, CancellationToken ct = default)
    {
        var titles = await (
            from i in _db.ArticleCacheItems
            where i.ArticleCache.ScopeKey == scopeKey
                && i.ArticleCache.ExpiresAt > DateTimeOffset.UtcNow
            join a in _db.Articles on i.ArticleId equals a.Id
            select a.Title
        ).ToListAsync(ct);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in titles)
        {
            var normalized = TitleNormalizer.Normalize(title);
            seen.Add(normalized);
        }

        return seen.Count;
    }

    public async Task<List<string?>> GetTitlesForScopeAsync(string scopeKey, CancellationToken ct = default)
    {
        return await (
            from i in _db.ArticleCacheItems
            where i.ArticleCache.ScopeKey == scopeKey
                && i.ArticleCache.ExpiresAt > DateTimeOffset.UtcNow
            join a in _db.Articles on i.ArticleId equals a.Id
            select a.Title
        ).ToListAsync(ct);
    }

    public async Task<int> GetHighestCachedPageAsync(string scopeKey, CancellationToken ct = default)
    {
        var maxPage = await _db.ArticleCaches
            .Where(c => c.ScopeKey == scopeKey && c.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(c => (int?)c.Page)
            .MaxAsync(ct);

        return maxPage ?? 0;
    }

    // Flat feed: order by Page ASC, Position ASC, then PublishedTime DESC
    public async Task<IReadOnlyList<(Guid ArticleId, string? Title, DateTime Published, int Page, int? Position)>>
         GetFlatFeedAsync(string scopeKey, int takeUpTo, CancellationToken ct = default)
    {
        var q =
            from i in _db.ArticleCacheItems
            where i.ArticleCache.ScopeKey == scopeKey
            && i.ArticleCache.ExpiresAt > DateTimeOffset.UtcNow
            join a in _db.Articles on i.ArticleId equals a.Id
            orderby i.ArticleCache.Page ascending, i.Position ascending, a.PublishedTime descending
            select new { i.ArticleId, a.Title, a.PublishedTime, i.ArticleCache.Page, i.Position };


        return await q.Take(takeUpTo)
            .Select(x => new ValueTuple<Guid, string?, DateTime, int, int?>(x.ArticleId, x.Title, x.PublishedTime, x.Page, x.Position))
            .ToListAsync(ct);
    }

    // Load full rows for a batch of ids (preserve external order on caller)
    public async Task<List<Article>> LoadArticlesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet();
        return await _db.Articles
            .Where(a => set.Contains(a.Id))
            .ToListAsync(ct);
    }

    public Task<bool> HasFreshPageAsync(string scopeKey, int page, DateTimeOffset now, CancellationToken ct = default)
    => _db.ArticleCaches
        .AnyAsync(c => c.ScopeKey == scopeKey && c.Page == page && c.ExpiresAt > now, ct);

}
