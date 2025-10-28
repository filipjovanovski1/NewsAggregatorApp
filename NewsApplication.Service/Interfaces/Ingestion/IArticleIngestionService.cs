using System.Threading;
using System.Threading.Tasks;
using NewsApplication.Domain.Cache;

namespace NewsApplication.Service.Interfaces.Ingestion;

public interface IArticleIngestionService
{
    Task<ArticleCache> FetchAndCachePageAsync(
        string scopeKey, int page, int pageSize, CancellationToken ct);
}
