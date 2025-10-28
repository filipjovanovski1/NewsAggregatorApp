using NewsApplication.Domain.Cache;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Ingestion;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NewsApplication.Service.Implementations.Ingestion;

public sealed class ArticleIngestionService : IArticleIngestionService
{
    private readonly INewsdataClient _client;
    private readonly IArticleRepository _repo;

    public ArticleIngestionService(INewsdataClient client, IArticleRepository repo)
    {
        _client = client;
        _repo = repo;
    }

    public async Task<ArticleCache> FetchAndCachePageAsync(
     string scopeKey, int page, int pageSize, CancellationToken ct)
    {
        var prevToken = await _repo.GetNextPageTokenForAsync(scopeKey, page, ct);

        var (articles, nextToken) = await _client.FetchPageAsync(scopeKey, prevToken, pageSize, ct);

        // --- Intra-page exact dedupe by (Title, Description) ---
        static string NormDesc(string? d) => d is null ? "\x01" : d; // sentinel only for keying equality
        var byKey = new Dictionary<(string Title, string DescKey), Domain.DomainModels.Article>();

        foreach (var a in articles)
        {
            var key = (a.Title ?? string.Empty, NormDesc(a.Description));
            if (!byKey.TryGetValue(key, out var first))
            {
                byKey[key] = a; // first occurrence becomes canonical
            }
            else
            {
                // Remap duplicate to canonical ArticleId so downstream links point to one row
                a.ArticleId = first.ArticleId;
            }
        }


        // Optionally collapse outgoing batch to unique ArticleId to save DB work:
        var finalBatch = byKey.Values
            .GroupBy(x => x.ArticleId, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        await _repo.UpsertAsync(finalBatch, ct);

        // Build cache using the (potentially remapped) IDs from the original order
        var cache = await _repo.PutPageAsync(
            scopeKey: scopeKey,
            page: page,
            nextPageToken: nextToken,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            items: articles.Select((a, i) => (a.ArticleId, (int?)i)).ToList(),
            ct: ct);

        return cache;
    }

}
