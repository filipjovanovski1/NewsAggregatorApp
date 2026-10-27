using NewsApplication.Domain.DTOs.Scopes;

namespace NewsApplication.Service.Interfaces;

public interface IStoredArticleSearchService
{
    Task<StoredArticleSearchResult> SearchAsync(
        string scopeKey,
        int offset,
        int limit,
        CancellationToken ct = default);
}
