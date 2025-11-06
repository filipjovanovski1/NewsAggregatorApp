using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.Cache;
using NewsApplication.Domain.DomainModels;
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
    //  - Upsert by ArticleId (your primary key)
    //  - Update mutable columns on conflict
    // -----------------------------
    public async Task UpsertAsync(IEnumerable<Article> articles, CancellationToken ct = default)
    {
        var rows = articles?.ToList() ?? new List<Article>();
        if (rows.Count == 0) return;

        // We'll do one batched INSERT ... ON CONFLICT (ArticleId) DO UPDATE
        // to keep it idempotent and efficient.
        var sb = new StringBuilder();
        sb.AppendLine(@"
        INSERT INTO ""Articles""
        (""ArticleId"", ""Provider"", ""Title"", ""Description"", ""ImageUrl"", ""Publisher"", 
        ""Url"", ""PublishedTime"", ""Categories"")
        VALUES");

        var parameters = new List<NpgsqlParameter>();
        for (int i = 0; i < rows.Count; i++)
        {
            var p = rows[i];
            if (i > 0) sb.AppendLine(",");

            sb.Append($"(@id{i}, @prov{i}, @title{i}, @desc{i}, @img{i}, @pubr{i}, @url{i}, @ptime{i}, @cats{i})");

            parameters.Add(new NpgsqlParameter($"id{i}", p.ArticleId));
            parameters.Add(new NpgsqlParameter($"prov{i}", p.Provider ?? "NEWSDATA")); // your note said set later
            parameters.Add(new NpgsqlParameter($"title{i}", p.Title ?? string.Empty));
            parameters.Add(new NpgsqlParameter($"desc{i}", (object?)p.Description ?? DBNull.Value));
            parameters.Add(new NpgsqlParameter($"img{i}", (object?)p.ImageUrl ?? DBNull.Value));
            parameters.Add(new NpgsqlParameter($"pubr{i}", p.Publisher ?? string.Empty));
            parameters.Add(new NpgsqlParameter($"url{i}", p.Url ?? string.Empty));
            parameters.Add(new NpgsqlParameter($"ptime{i}", p.PublishedTime));
            parameters.Add(new NpgsqlParameter($"cats{i}", NpgsqlTypes.NpgsqlDbType.Jsonb)
            {
                Value = p.Categories ?? new List<string>()
            });
        }

        sb.AppendLine(@"
        ON CONFLICT (""ArticleId"") DO UPDATE SET
          ""Provider""      = EXCLUDED.""Provider"",
          ""Title""         = EXCLUDED.""Title"",
          ""Description""   = EXCLUDED.""Description"",
          ""ImageUrl""      = EXCLUDED.""ImageUrl"",
          ""Publisher""     = EXCLUDED.""Publisher"",
          ""Url""           = EXCLUDED.""Url"",
          ""PublishedTime"" = EXCLUDED.""PublishedTime"",
          ""Categories""    = EXCLUDED.""Categories"";");

        var sql = sb.ToString();
        await _db.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), ct);
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
    IReadOnlyList<(string articleId, int? position)> items,
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
            WHERE i.""ArticleId"" = a.""ArticleId""
        )
        AND a.""InsertedAt"" < (now() - {safetyWindow});", ct);
    }
    // Count distinct articles across all cached pages for a scope
    public async Task<int> CountDistinctForScopeAsync(string scopeKey, CancellationToken ct = default)
    {
        return await _db.ArticleCacheItems
            .Where(i => i.ArticleCache.ScopeKey == scopeKey)
            .Select(i => i.ArticleId)
            .Distinct()
            .CountAsync(ct);
    }

    // Flat feed: order by Page ASC, Position ASC, then PublishedTime DESC
    public async Task<IReadOnlyList<(string ArticleId, DateTime Published, int Page, int? Position)>>
        GetFlatFeedAsync(string scopeKey, int takeUpTo, CancellationToken ct = default)
    {
        var q =
            from i in _db.ArticleCacheItems
            where i.ArticleCache.ScopeKey == scopeKey
            && i.ArticleCache.ExpiresAt > DateTimeOffset.UtcNow
            join a in _db.Articles on i.ArticleId equals a.ArticleId
            orderby i.ArticleCache.Page ascending, i.Position ascending, a.PublishedTime descending
            select new { i.ArticleId, a.PublishedTime, i.ArticleCache.Page, i.Position };


        return await q.Take(takeUpTo)
            .Select(x => new ValueTuple<string, DateTime, int, int?>(x.ArticleId, x.PublishedTime, x.Page, x.Position))
            .ToListAsync(ct);
    }

    // Load full rows for a batch of ids (preserve external order on caller)
    public async Task<List<Article>> LoadArticlesByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        return await _db.Articles
            .Where(a => set.Contains(a.ArticleId))
            .ToListAsync(ct);
    }

    public Task<bool> HasFreshPageAsync(string scopeKey, int page, DateTimeOffset now, CancellationToken ct = default)
    => _db.ArticleCaches
        .AnyAsync(c => c.ScopeKey == scopeKey && c.Page == page && c.ExpiresAt > now, ct);

}
