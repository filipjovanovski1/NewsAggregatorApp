using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NewsApplication.Service.Implementations.Client;

public sealed class NewsdataClient : INewsdataClient
{
    private readonly HttpClient _http;
    private readonly NewsdataOptions _opt;
    private const string ProviderName = "NEWSDATA";
    private readonly ILogger<NewsdataClient> _logger;
    public NewsdataClient(HttpClient http, IOptions<NewsdataOptions> opt, ILogger<NewsdataClient> logger)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger;
    }

    private static string RedactKey(string url)
    {
        var i = url.IndexOf("apikey=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return url;
        var end = url.IndexOf('&', i);
        return end < 0
            ? url[..i] + "apikey=****"
            : url[..i] + "apikey=****" + url[end..];
    }

    public async Task<(List<Article> articles, string? nextPageToken)> FetchPageAsync(
        string scopeKey, string? pageToken, int pageSize, CancellationToken ct)
    {
        var url = BuildUrl(scopeKey, pageToken, pageSize);

        _logger.LogInformation("Newsdata GET {Url} (scopeKey={Scope})",
        RedactKey(url), scopeKey);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var articles = new List<Article>();
        string? nextToken = null;

        if (root.ValueKind == JsonValueKind.Array)
        {
            // Your sample: the response is a bare array of items
            foreach (var x in root.EnumerateArray())
                TryAddArticleFromItem(x, articles);
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // Common Newsdata format: { results: [...], nextPage: "..." }
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in results.EnumerateArray())
                    TryAddArticleFromItem(x, articles);
            }
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in data.EnumerateArray())
                    TryAddArticleFromItem(x, articles);
            }

            if (root.TryGetProperty("nextPage", out var np) && np.ValueKind == JsonValueKind.String)
                nextToken = np.GetString();
            else if (root.TryGetProperty("nextPageToken", out var npt) && npt.ValueKind == JsonValueKind.String)
                nextToken = npt.GetString();
        }

        return (articles, nextToken);
    }

    private string BuildUrl(string scopeKey, string? pageToken, int pageSize)
    {
        // BaseUrl like "https://newsdata.io/api/1/latest"
        var baseUrl = _http.BaseAddress?.ToString();
        var endpoint = string.IsNullOrWhiteSpace(baseUrl) ? _opt.BaseUrl : baseUrl.TrimEnd('/');

        var qp = new List<string>
        {
            $"apikey={Uri.EscapeDataString(_opt.ApiKey)}"
        };

        string? qTerm = null;
        string? localTerm = null;
        string? citySlug = null;

        // Newsdata typically uses numeric "page" OR token "nextPage" depending on endpoint/version.
        if (!string.IsNullOrWhiteSpace(pageToken))
        {
            if (pageToken.All(char.IsDigit))
                qp.Add($"page={pageToken}");
            else
                qp.Add($"nextPage={Uri.EscapeDataString(pageToken)}");
        }

        // Scope parsing: support simple filters you care about.
        foreach (var segment in scopeKey.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = segment.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            if (kv.Length != 2) continue;

            var key = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();
            var rawVal = Uri.UnescapeDataString(val);

            switch (key)
            {
                case "country":
                    // newsdata expects ISO2 (lowercase). Keep whatever the resolver provided,
                    // but normalize to lower to be safe.
                    {
                        qp.Add($"country={Uri.EscapeDataString(
                        (rawVal ?? string.Empty).ToLowerInvariant())}");
                    }
                    break;
                case "category":
                case "cats":
                    qp.Add($"category={Uri.EscapeDataString(rawVal)}");
                    break;
                case "language":
                case "lang":
                    qp.Add($"language={Uri.EscapeDataString(rawVal)}");
                    break;
                case "q": // free-text search
                    qTerm = string.IsNullOrWhiteSpace(qTerm) ? rawVal : $"{qTerm} {rawVal}";
                    break;
                case "local":
                    localTerm = string.IsNullOrWhiteSpace(localTerm) ? rawVal : $"{localTerm} {rawVal}";
                    break;
                case "city":
                    // capture slug to recover the primary (Latin) city name
                    citySlug = rawVal;
                    break;
            }
        }
        // inside BuildUrl after collecting localTerm and qTerm
        string? combinedQ = null;

        // Derive English/Latin city name from slug (city:skopje-mk -> "skopje")
        string? cityNameFromSlug = null;
        if (!string.IsNullOrWhiteSpace(citySlug))
        {
            var slug = citySlug.Trim();
            // drop trailing "-xx" (iso2) if present
            var maybeName = slug;
            if (slug.Length > 3 && slug[^3] == '-' && slug[^2..].All(char.IsLetter))
            {
                maybeName = slug[..^3];
            }
            cityNameFromSlug = maybeName.Replace('-', ' ').Trim();
        }

        string? keywords = null;
        string? englishForOr = null;

        if (!string.IsNullOrWhiteSpace(qTerm))
        {
            var qt = qTerm.Trim();
            if (!string.IsNullOrWhiteSpace(cityNameFromSlug))
            {
                var cityNorm = cityNameFromSlug.ToLowerInvariant();
                var qtNorm = qt.ToLowerInvariant();
                if (qtNorm.StartsWith(cityNorm))
                {
                    keywords = qt[cityNameFromSlug.Length..].Trim();
                    englishForOr = cityNameFromSlug;
                }
                else
                {
                    keywords = qt;
                    englishForOr = cityNameFromSlug;
                }
            }
            else
            {
                keywords = qt;
            }
        }

        // Build combined query:
        // - If localTerm and englishForOr exist: "(local) OR (english)" + " " + keywords
        // - Else fallback to localTerm or qTerm keywords
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(localTerm) && !string.IsNullOrWhiteSpace(englishForOr))
        {
            parts.Add($"({localTerm.Trim()}) OR ({englishForOr.Trim()})");
        }
        else if (!string.IsNullOrWhiteSpace(localTerm))
        {
            parts.Add(localTerm.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(englishForOr))
        {
            parts.Add(englishForOr.Trim());
        }

        if (!string.IsNullOrWhiteSpace(keywords))
        {
            parts.Add(keywords.Trim());
        }

        combinedQ = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        if (!string.IsNullOrWhiteSpace(combinedQ))
        {
            qp.Add($"q={Uri.EscapeDataString(combinedQ)}");
        }

        // pageSize is not always supported by Newsdata (many endpoints fix size),
        // so we omit it to avoid errors. If you confirm support, add it here.

        var query = string.Join("&", qp);
        return $"{endpoint}?{query}";
    }

    private static void TryAddArticleFromItem(JsonElement x, List<Article> dest)
    {
        // Field names matched to your sample exactly, with safe fallbacks.
        var providerArticleId = GetString(x, "article_id") ?? GetString(x, "id");
        var title = GetString(x, "title") ?? string.Empty;
        var desc = GetString(x, "description") ?? GetString(x, "content");
        var imageUrl = GetString(x, "image_url") ?? GetString(x, "imageUrl");
        var publisher = GetString(x, "source_name") ?? GetString(x, "source_id") ?? string.Empty;
        var link = GetString(x, "link") ?? GetString(x, "url") ?? string.Empty;

        var published = TryGetDateTime(x, "pubDate")
                        ?? TryGetDateTime(x, "published_at")
                        ?? TryGetDateTime(x, "published_at_utc")
                        ?? DateTime.UtcNow;

        var categories = TryGetStringArray(x, "category")
                         ?? TryGetStringArray(x, "categories")
                         ?? new List<string>();

        dest.Add(new Article
        {
            Provider = "NEWSDATA",
            ProviderArticleId = providerArticleId,
            Title = title,
            Description = desc,
            ImageUrl = imageUrl,
            Publisher = publisher,
            Url = link,
            PublishedTime = published,
            Categories = categories
        });
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? TryGetDateTime(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;

        if (v.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                v.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return timestamp.UtcDateTime;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var unixSec))
            return DateTimeOffset.FromUnixTimeSeconds(unixSec).UtcDateTime;

        return null;
    }

    private static List<string>? TryGetStringArray(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;

        return v.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
    }
}
