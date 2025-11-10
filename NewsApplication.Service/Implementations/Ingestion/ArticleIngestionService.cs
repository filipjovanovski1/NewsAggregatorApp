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

    private static (string providerScopeKey, string? keywords) SplitKw(string scopeKey)
    {
        const string tag = "|kw:";
        var idx = scopeKey.IndexOf(tag, StringComparison.Ordinal);
        if (idx < 0) return (scopeKey, null);

        var baseKey = scopeKey.Substring(0, idx);
        var enc = scopeKey.Substring(idx + tag.Length);
        var kw = Uri.UnescapeDataString(enc);
        return (baseKey, string.IsNullOrWhiteSpace(kw) ? null : kw);
    }

    private static bool MatchesKeywords(NewsApplication.Domain.DomainModels.Article a, string keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return true;
        var terms = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim().ToLowerInvariant())
                            .ToArray();
        if (terms.Length == 0) return true;

        string s(string? v) => (v ?? string.Empty).ToLowerInvariant();

        var hayTitle = s(a.Title);
        var hayDesc = s(a.Description);
        var hayPub = s(a.Publisher);
        var hayCats = string.Join(' ', (a.Categories ?? new List<string>()).Select(c => c.ToLowerInvariant()));

        // match if ALL terms appear in any of the fields (title/desc/publisher/categories)
        foreach (var t in terms)
        {
            if (!(hayTitle.Contains(t) || hayDesc.Contains(t) || hayPub.Contains(t) || hayCats.Contains(t)))
                return false;
        }
        return true;
    }

    public ArticleIngestionService(INewsdataClient client, IArticleRepository repo)
    {
        _client = client;
        _repo = repo;
    }

    public async Task<ArticleCache> GetOrFetchPageAsync(
    string scopeKey, int page, int pageSize, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Safeguard: if fresh, DO NOT call the API
        if (await _repo.HasFreshPageAsync(scopeKey, page, now, ct))
            return (await _repo.GetPageAsync(scopeKey, page, ct))!;  // return the full cached page

        // Miss or expired → fetch + cache (this will set ExpiresAt = now + 10m)
        return await FetchAndCachePageAsync(scopeKey, page, pageSize, ct);
    }

    // ArticleIngestionService.cs
    private static string MergeKwIntoProviderScope(string providerScopeKey, string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return providerScopeKey;

        // If there's already a q:, append the keywords to it; otherwise add q:
        var parts = providerScopeKey.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        var qIndex = parts.FindIndex(p => p.StartsWith("q:", StringComparison.OrdinalIgnoreCase));

        if (qIndex >= 0)
        {
            var existing = parts[qIndex].Substring(2);
            var merged = string.IsNullOrWhiteSpace(existing) ? keywords : $"{existing} {keywords}";
            parts[qIndex] = "q:" + merged.Trim();
        }
        else
        {
            parts.Add("q:" + keywords.Trim());
        }

        return string.Join('|', parts);
    }


    public async Task<ArticleCache> FetchAndCachePageAsync(
     string scopeKey, int page, int pageSize, CancellationToken ct)
    {
        var (providerScopeKey, keywords) = SplitKw(scopeKey);

        var prevToken = await _repo.GetNextPageTokenForAsync(scopeKey, page, ct);

        var providerForClient = MergeKwIntoProviderScope(providerScopeKey, keywords);

        var (articles, nextToken) = await _client.FetchPageAsync(providerForClient, prevToken, pageSize, ct);

        if (!string.IsNullOrWhiteSpace(keywords))
            articles = articles.Where(a => MatchesKeywords(a, keywords!)).ToList();
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
