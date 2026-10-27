using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NewsApplication.Domain.DTOs.Scopes;

namespace NewsApplication.Domain.Helpers;

public static class ArticleScopeKey
{
    public static StoredArticleSearchCriteria Parse(string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
            return new StoredArticleSearchCriteria(null, null, []);

        var segments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in scopeKey.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf(':');
            if (separator <= 0 || separator == segment.Length - 1)
                continue;

            var key = segment[..separator].Trim();
            var encodedValue = segment[(separator + 1)..].Trim();
            if (key.Length > 0 && encodedValue.Length > 0)
                segments[key] = Uri.UnescapeDataString(encodedValue);
        }

        segments.TryGetValue("country", out var country);
        segments.TryGetValue("city", out var citySlug);
        segments.TryGetValue("kw", out var keywordText);
        segments.TryGetValue("q", out var queryText);

        country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
        citySlug = string.IsNullOrWhiteSpace(citySlug) ? null : citySlug.Trim().ToLowerInvariant();

        // City-only scopes contain q:<city name>. That locates the scope; it is not an
        // article keyword. A real keyword is carried in kw:, or in q: for country/global scopes.
        if (string.IsNullOrWhiteSpace(keywordText) && !string.IsNullOrWhiteSpace(queryText))
        {
            var cityRoot = CityRoot(citySlug, country);
            if (cityRoot is null || !string.Equals(Slugify(queryText), cityRoot, StringComparison.Ordinal))
                keywordText = queryText;
        }

        var keywords = (keywordText ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return new StoredArticleSearchCriteria(country, citySlug, keywords);
    }

    public static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        var folded = builder.ToString().Normalize(NormalizationForm.FormC);
        folded = Regex.Replace(folded, @"[^a-z0-9\s\-]", string.Empty);
        return Regex.Replace(folded, @"[\s\-_]+", "-").Trim('-');
    }

    private static string? CityRoot(string? citySlug, string? countryIso2)
    {
        if (string.IsNullOrWhiteSpace(citySlug))
            return null;

        var suffix = string.IsNullOrWhiteSpace(countryIso2)
            ? null
            : $"-{countryIso2.ToLowerInvariant()}";
        return suffix is not null && citySlug.EndsWith(suffix, StringComparison.Ordinal)
            ? citySlug[..^suffix.Length]
            : citySlug;
    }
}
