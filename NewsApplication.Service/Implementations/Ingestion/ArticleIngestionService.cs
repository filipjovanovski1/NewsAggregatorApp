using NewsApplication.Domain.Cache;
using NewsApplication.Domain.Helpers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Ingestion;
using System;
using System.Collections.Generic;
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
    private static string NormalizeTitle(string? title)
        => string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
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
            await _repo.PruneDuplicateTitlesForCacheAsync(scopeKey, page, ct);
        var cached = await _repo.GetPageAsync(scopeKey, page, ct);
        if (cached is not null)
            return cached;

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
            var merged = MergeTerms(existing, keywords);
            parts[qIndex] = "q:" + merged;
        }
        else
        {
            parts.Add("q:" + keywords.Trim());
        }

        return string.Join('|', parts);
    }
    private static string MergeTerms(string existing, string keywords)
    {
        static IEnumerable<string> Terms(string value)
            => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var term in Terms(existing))
        {
            if (seen.Add(term)) ordered.Add(term);
        }

        foreach (var term in Terms(keywords))
        {
            if (seen.Add(term)) ordered.Add(term);
        }

        return string.Join(' ', ordered);
    }

    public async Task<ArticleCache> FetchAndCachePageAsync(
     string scopeKey, int page, int pageSize, CancellationToken ct)
    {
        var (providerScopeKey, keywords) = SplitKw(scopeKey);

        var prevToken = await _repo.GetNextPageTokenForAsync(scopeKey, page, ct);

        var providerForClient = MergeKwIntoProviderScope(providerScopeKey, keywords);

        var existingTitles = await _repo.GetTitlesForScopeAsync(scopeKey, ct);
        var seenTitles = new HashSet<string>(existingTitles.Select(TitleNormalizer.Normalize), StringComparer.OrdinalIgnoreCase);
        var uniqueArticles = new List<Domain.DomainModels.Article>();

        var (batch, nextToken) = await _client.FetchPageAsync(providerForClient, prevToken, pageSize, ct);

        if (!string.IsNullOrWhiteSpace(keywords))
            batch = batch.Where(a => MatchesKeywords(a, keywords!)).ToList();

        foreach (var article in batch)
        {
            var normalizedTitle = TitleNormalizer.Normalize(article.Title);
            if (!seenTitles.Add(normalizedTitle))
                continue;

            uniqueArticles.Add(article);
        }


        var finalNextToken = nextToken;

        await _repo.UpsertAsync(uniqueArticles, ct);

        // Build cache using the (potentially remapped) IDs from the original order
        await _repo.PutPageAsync(
            scopeKey: scopeKey,
             page: page,
             nextPageToken: finalNextToken,
             expiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
             items: uniqueArticles.Select((a, i) => (a.ArticleId, (int?)i)).ToList(),
             ct: ct);


        await _repo.PruneDuplicateTitlesForCacheAsync(scopeKey, page, ct);

        return (await _repo.GetPageAsync(scopeKey, page, ct))!;
    }

}
