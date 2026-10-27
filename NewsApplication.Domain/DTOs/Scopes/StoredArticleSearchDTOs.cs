using NewsApplication.Domain.DomainModels;

namespace NewsApplication.Domain.DTOs.Scopes;

public sealed record StoredArticleSearchCriteria(
    string? CountryIso2,
    string? CitySlug,
    IReadOnlyList<string> Keywords);

public sealed record StoredArticleSearchResult(
    IReadOnlyList<Article> Items,
    int Total);
