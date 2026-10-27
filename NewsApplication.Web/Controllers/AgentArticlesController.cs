using Microsoft.AspNetCore.Mvc;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Domain.Helpers;
using NewsApplication.Repository.Db.Interfaces;
using System.Text.RegularExpressions;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/internal/agent/articles")]
public sealed class AgentArticlesController : ControllerBase
{
    private const int CandidateLimit = 200;
    private const int MaxContentLength = 12_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "about", "for", "from", "in", "is", "it", "latest",
        "me", "news", "of", "on", "show", "the", "to", "what", "with", "current",
        "situation", "state"
    };

    private static readonly IReadOnlyDictionary<string, string[]> TopicExpansions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["economic"] = ["економ", "стопан", "финанс", "бизнис", "буџет", "инфлац", "плат", "вработ", "ekonom", "financ", "biznes", "buxhet", "inflacion", "pag", "punës"],
            ["economy"] = ["економ", "стопан", "финанс", "бизнис", "буџет", "инфлац", "плат", "вработ", "ekonom", "financ", "biznes", "buxhet", "inflacion", "pag", "punës"],
            ["economics"] = ["економ", "стопан", "финанс", "бизнис", "буџет", "инфлац", "плат", "вработ", "ekonom", "financ", "biznes", "buxhet", "inflacion", "pag", "punës"],
            ["finance"] = ["финанс", "банка", "буџет", "financ", "bank", "buxhet"],
            ["financial"] = ["финанс", "банка", "буџет", "financ", "bank", "buxhet"],
            ["business"] = ["бизнис", "компан", "фирм", "biznes", "kompan"],
            ["inflation"] = ["инфлац", "цени", "inflacion", "çmim"],
            ["employment"] = ["вработ", "работни места", "невработ", "punës", "papun"],
            ["unemployment"] = ["невработ", "вработ", "papun", "punës"],
            ["jobs"] = ["вработ", "работни места", "punës"],
            ["salary"] = ["плат", "заработувач", "pag"],
            ["wages"] = ["плат", "заработувач", "pag"]
        };

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        AgentArticleSearchRequest request,
        [FromServices] IArticleRepository articles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ScopeKey))
            return BadRequest(new { error = "scopeKey is required" });

        var parsed = ArticleScopeKey.Parse(request.ScopeKey);
        var geographicCriteria = new StoredArticleSearchCriteria(
            parsed.CountryIso2,
            parsed.CitySlug,
            []);
        var candidates = await articles.SearchStoredAsync(
            geographicCriteria,
            0,
            CandidateLimit,
            ct);

        var cityTerms = CityTerms(request.ScopeKey, parsed.CitySlug, parsed.CountryIso2);
        var scopedCandidates = cityTerms.Count == 0
            ? candidates.Items
            : candidates.Items.Where(article => MatchesCity(article, cityTerms)).ToList();

        var terms = ExpandTerms(Tokenize(request.Query));

        var scored = scopedCandidates
            .Select(article => new { Article = article, Score = Score(article, terms) })
            .Where(item => terms.Count == 0 || item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Article.PublishedTime)
            .ToList();

        var usedRecentFallback = terms.Count > 0 && scored.Count == 0;
        var ranked = (usedRecentFallback
                ? scopedCandidates
                    .OrderByDescending(article => article.PublishedTime)
                    .Select(article => new { Article = article, Score = 0 })
                : scored)
            .Take(Math.Clamp(request.Limit, 1, 20))
            .Select(item => ToSearchItem(item.Article, item.Score))
            .ToList();

        return Ok(new
        {
            request.ScopeKey,
            scopeLabel = parsed.CountryIso2,
            query = request.Query?.Trim() ?? string.Empty,
            usedRecentFallback,
            total = ranked.Count,
            items = ranked
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        [FromServices] IArticleRepository articles,
        CancellationToken ct)
    {
        var article = await articles.GetStoredByIdAsync(id, ct);
        if (article is null)
            return NotFound();

        return Ok(new
        {
            articleId = article.Id,
            article.Provider,
            article.Title,
            article.Description,
            contentText = Truncate(article.ContentText, MaxContentLength),
            article.Publisher,
            article.Url,
            publishedTime = article.PublishedTime,
            article.Author,
            article.Language,
            article.Categories
        });
    }

    private static object ToSearchItem(Article article, int score) => new
    {
        articleId = article.Id,
        article.Provider,
        article.Title,
        description = Truncate(article.Description ?? article.ContentText, 1_500),
        article.Publisher,
        article.Url,
        publishedTime = article.PublishedTime,
        article.Categories,
        relevanceScore = score
    };

    private static List<string> Tokenize(string? query) =>
        Regex.Matches((query ?? string.Empty).ToLowerInvariant(), @"[\p{L}\p{N}]{2,}")
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static List<string> ExpandTerms(IReadOnlyList<string> terms)
    {
        var expanded = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (TopicExpansions.TryGetValue(term, out var aliases))
                expanded.UnionWith(aliases);
        }

        return expanded.Take(32).ToList();
    }

    private static List<string> CityTerms(string scopeKey, string? citySlug, string? countryIso2)
    {
        if (string.IsNullOrWhiteSpace(citySlug))
            return [];

        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suffix = string.IsNullOrWhiteSpace(countryIso2)
            ? string.Empty
            : $"-{countryIso2.ToLowerInvariant()}";
        var cityRoot = suffix.Length > 0 && citySlug.EndsWith(suffix, StringComparison.Ordinal)
            ? citySlug[..^suffix.Length]
            : citySlug;
        if (!string.IsNullOrWhiteSpace(cityRoot))
            terms.Add(cityRoot.Replace('-', ' '));

        var localName = ScopeValue(scopeKey, "local");
        if (!string.IsNullOrWhiteSpace(localName))
            terms.Add(localName);

        return terms.ToList();
    }

    private static string? ScopeValue(string scopeKey, string name)
    {
        foreach (var segment in scopeKey.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf(':');
            if (separator <= 0 || !segment[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return Uri.UnescapeDataString(segment[(separator + 1)..]);
        }

        return null;
    }

    private static bool MatchesCity(Article article, IReadOnlyList<string> cityTerms) =>
        cityTerms.Any(term =>
            Contains(article.Title, term) ||
            Contains(article.Description, term) ||
            Contains(article.ContentText, term) ||
            article.Categories.Any(category => Contains(category, term)));

    private static int Score(Article article, IReadOnlyList<string> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            if (Contains(article.Title, term)) score += 8;
            if (Contains(article.Publisher, term)) score += 3;
            if (article.Categories.Any(category => Contains(category, term))) score += 5;
            if (Contains(article.Description, term)) score += 3;
            if (Contains(article.ContentText, term)) score += 1;
        }
        return score;
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxLength
            ? value
            : value[..maxLength] + "…";

    public sealed class AgentArticleSearchRequest
    {
        public string ScopeKey { get; set; } = string.Empty;
        public string? Query { get; set; }
        public int Limit { get; set; } = 12;
    }
}
