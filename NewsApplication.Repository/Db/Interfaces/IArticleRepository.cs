using NewsApplication.Domain.Cache;
using NewsApplication.Domain.DomainModels;

namespace NewsApplication.Repository.Db.Interfaces;

public interface IArticleRepository
{
    /// <summary>
    /// Upsert by ArticleId (your ArticleId is the provider's external ID).
    /// </summary>
    Task UpsertAsync(IEnumerable<Article> articles, CancellationToken ct = default);

    /// <summary>
    /// Loads ArticleCache + Items (+ Articles) for (scopeKey, page).
    /// </summary>
    Task<ArticleCache?> GetPageAsync(string scopeKey, int page, CancellationToken ct = default);

    /// <summary>
    /// Insert/update the ArticleCache row, then idempotently link items (ON CONFLICT DO NOTHING).
    /// Returns the ArticleCache.
    /// </summary>
    Task<ArticleCache> PutPageAsync(
        string scopeKey,
        int page,
        string? nextPageToken,
        DateTimeOffset expiresAt,
        IReadOnlyList<(string articleId, int? position)> items,
        CancellationToken ct = default);

    /// <summary>
    /// Get the cursor token for the requested page from (page-1).
    /// </summary>
    Task<string?> GetNextPageTokenForAsync(string scopeKey, int page, CancellationToken ct = default);

    /// <summary>
    /// TTL cleanup: delete expired ArticleCache (CASCADE clears ArticleCacheItem).
    /// </summary>
    Task<int> DeleteExpiredCachesAsync(CancellationToken ct = default);

    /// <summary>
    /// GC Articles no longer referenced by any ArticleCacheItem (with safety window).
    /// </summary>
    /// 
    Task<int> DeleteOrphanArticlesAsync(TimeSpan safetyWindow, CancellationToken ct = default);

    Task<int> CountDistinctForScopeAsync(string scopeKey, CancellationToken ct = default);
    Task<IReadOnlyList<(string ArticleId, DateTime Published, int Page, int? Position)>>
        GetFlatFeedAsync(string scopeKey, int takeUpTo, CancellationToken ct = default);
    Task<List<Article>> LoadArticlesByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);

    Task<bool> HasFreshPageAsync(string scopeKey, int page, DateTimeOffset now, CancellationToken ct = default);

}
