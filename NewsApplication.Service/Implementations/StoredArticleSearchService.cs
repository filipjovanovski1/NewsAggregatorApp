using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Domain.Helpers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;

namespace NewsApplication.Service.Implementations;

public sealed class StoredArticleSearchService : IStoredArticleSearchService
{
    private readonly IArticleRepository _articles;

    public StoredArticleSearchService(IArticleRepository articles) => _articles = articles;

    public Task<StoredArticleSearchResult> SearchAsync(
        string scopeKey,
        int offset,
        int limit,
        CancellationToken ct = default) =>
        _articles.SearchStoredAsync(
            ArticleScopeKey.Parse(scopeKey),
            Math.Max(0, offset),
            Math.Clamp(limit, 1, 100),
            ct);
}
