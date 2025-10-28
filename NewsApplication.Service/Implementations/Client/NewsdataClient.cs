using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NewsApplication.Service.Implementations.Client;

public sealed class NewsdataClient : INewsdataClient
{
    private readonly HttpClient _http;
    private readonly NewsdataOptions _opt;
    private const string ProviderName = "NEWSDATA";

    public NewsdataClient(HttpClient http, IOptions<NewsdataOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public async Task<(List<Article> articles, string? nextPageToken)> FetchPageAsync(
        string scopeKey, string? pageToken, int pageSize, CancellationToken ct)
    {
        var url = BuildUrl(scopeKey, pageToken, pageSize);

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

            switch (key)
            {
                case "country":
                    qp.Add($"country={Uri.EscapeDataString(val)}");
                    break;
                case "category":
                case "cats":
                    qp.Add($"category={Uri.EscapeDataString(val)}");
                    break;
                case "language":
                case "lang":
                    qp.Add($"language={Uri.EscapeDataString(val)}");
                    break;
                case "q": // free-text search
                    qp.Add($"q={Uri.EscapeDataString(val)}");
                    break;
            }
        }

        // pageSize is not always supported by Newsdata (many endpoints fix size),
        // so we omit it to avoid errors. If you confirm support, add it here.

        var query = string.Join("&", qp);
        return $"{endpoint}?{query}";
    }

    private static void TryAddArticleFromItem(JsonElement x, List<Article> dest)
    {
        // Field names matched to your sample exactly, with safe fallbacks.
        var id = GetString(x, "article_id") ?? GetString(x, "id") ?? Guid.NewGuid().ToString("N");
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
            ArticleId = id,
            Provider = "NEWSDATA",
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

        if (v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
            return dt;

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
