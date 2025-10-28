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
            .Include(c => c.Items)
                .ThenInclude(i => i.Article)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ScopeKey == scopeKey && c.Page == page, ct);
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
        // 1) Upsert ArticleCache by (ScopeKey, Page)
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
            await _db.SaveChangesAsync(ct); // ensure Id for items
        }
        else
        {
            cache.NextPageToken = nextPageToken;
            cache.ExpiresAt = expiresAt;
            await _db.SaveChangesAsync(ct);
        }

        // 2) Idempotent link insert (batch)
        if (items.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(@"INSERT INTO ""ArticleCacheItems"" (""ArticleCacheId"", ""ArticleId"", ""Position"") VALUES");
            var parameters = new List<NpgsqlParameter>();

            for (int i = 0; i < items.Count; i++)
            {
                var (articleId, pos) = items[i];
                if (i > 0) sb.AppendLine(",");
                sb.Append($"(@cid{i}, @aid{i}, @pos{i})");

                parameters.Add(new NpgsqlParameter($"cid{i}", cache.Id));
                parameters.Add(new NpgsqlParameter($"aid{i}", articleId));
                parameters.Add(new NpgsqlParameter($"pos{i}", (object?)pos ?? DBNull.Value));
            }

            sb.AppendLine(@"
            ON CONFLICT (""ArticleCacheId"", ""ArticleId"") DO NOTHING;");

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
    public async Task<int> DeleteExpiredCachesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var sql = @"DELETE FROM ""ArticleCaches"" WHERE ""ExpiresAt"" < @now;";
        var count = await _db.Database.ExecuteSqlRawAsync(
            sql,
            new NpgsqlParameter("now", now),
            ct);
        return count;
    }

    // -----------------------------
    //  DeleteOrphanArticlesAsync(olderThan)
    //  - Remove Articles with no ArticleCacheItem references (with safety window)
    // -----------------------------
    public async Task<int> DeleteOrphanArticlesAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        var sql = @"
        DELETE FROM ""Articles"" a
        WHERE NOT EXISTS (
            SELECT 1 FROM ""ArticleCacheItems"" i
            WHERE i.""ArticleId"" = a.""ArticleId""
        )
        AND a.""InsertedAt"" < @olderThan;";
        var count = await _db.Database.ExecuteSqlRawAsync(
            sql,
            new NpgsqlParameter("olderThan", olderThan),
            ct);
        return count;
    }
}
