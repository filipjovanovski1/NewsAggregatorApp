using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Repository.Db.Interfaces.Discovery;

namespace NewsApplication.Service.Implementations.Discovery.Workers;

public sealed class RssPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpClients;
    private readonly DiscoverySchedulerOptions _options;
    private readonly ILogger<RssPollingWorker> _logger;

    public RssPollingWorker(
        IServiceScopeFactory scopes,
        IHttpClientFactory httpClients,
        IOptions<DiscoverySchedulerOptions> options,
        ILogger<RssPollingWorker> logger)
    {
        _scopes = scopes;
        _httpClients = httpClients;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        await RunOnce(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnce(stoppingToken);
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var sources = scope.ServiceProvider.GetRequiredService<INewsSourceRepository>();
            var articles = scope.ServiceProvider.GetRequiredService<IArticleRepository>();
            var now = DateTimeOffset.UtcNow;
            var feeds = await sources.GetDueFeedsForPollingAsync(
                now, Math.Max(1, _options.PollBatchSize), ct);

            foreach (var feed in feeds.Where(x => IsDue(x, now)))
                await PollFeed(feed, articles, now, ct);

            await sources.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "RSS polling cycle failed");
        }
    }

    private async Task PollFeed(
        NewsSourceFeed feed,
        IArticleRepository articleRepository,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
            if (!string.IsNullOrWhiteSpace(feed.LastEtag) &&
                EntityTagHeaderValue.TryParse(feed.LastEtag, out var etag))
                request.Headers.IfNoneMatch.Add(etag);

            using var response = await _httpClients.CreateClient("rss").SendAsync(request, ct);
            feed.LastPolledAt = now;

            if (response.StatusCode == HttpStatusCode.NotModified)
                return;

            response.EnsureSuccessStatusCode();
            if (response.Headers.ETag is not null)
                feed.LastEtag = response.Headers.ETag.ToString();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
            var parsed = ParseArticles(document, feed.NewsSource).Take(100).ToList();
            if (parsed.Count > 0)
                await articleRepository.UpsertAsync(parsed, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Record the attempt so a broken feed follows its tier cadence instead of being
            // hammered on every ten-minute worker tick. Nightly validation owns IsActive.
            feed.LastPolledAt = now;
            _logger.LogWarning(exception, "RSS poll failed for feed {FeedId} ({Url})", feed.Id, feed.Url);
        }
    }

    private static IEnumerable<Article> ParseArticles(XDocument document, NewsSource? source)
    {
        var entries = document.Descendants()
            .Where(x => x.Name.LocalName is "item" or "entry");

        foreach (var entry in entries)
        {
            var title = Value(entry, "title");
            var link = AtomLink(entry) ?? Value(entry, "link");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                continue;

            var id = Value(entry, "guid") ?? Value(entry, "id") ?? link;
            var published = ParseDate(
                Value(entry, "pubDate") ??
                Value(entry, "published") ??
                Value(entry, "updated"));
            var categories = entry.Elements()
                .Where(x => x.Name.LocalName == "category")
                .Select(x => x.Attribute("term")?.Value ?? x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            yield return new Article
            {
                ProviderArticleId = id.Trim(),
                Provider = "RSS",
                Title = title.Trim(),
                Description = Value(entry, "description") ?? Value(entry, "summary") ?? Value(entry, "content"),
                Publisher = source?.Name ?? source?.Domain ?? "RSS",
                Url = link.Trim(),
                PublishedTime = published.UtcDateTime,
                Categories = categories
            };
        }
    }

    private static bool IsDue(NewsSourceFeed feed, DateTimeOffset now)
    {
        var liveTiers = feed.NewsSource?.Scopes
            .Where(x => !x.IsStale)
            .Select(x => x.PollingTier)
            .ToList() ?? [];
        if (liveTiers.Count == 0)
            return false;

        var cadence = liveTiers.Min(TierMinutes);
        return feed.LastPolledAt is null || feed.LastPolledAt <= now.AddMinutes(-cadence);
    }

    private static int TierMinutes(string? tier) => tier switch
    {
        PollingTiers.High => 10,
        PollingTiers.Medium => 30,
        PollingTiers.Low => 120,
        _ => 360
    };

    private static string? Value(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value;

    private static string? AtomLink(XElement entry) =>
        entry.Elements()
            .Where(x => x.Name.LocalName == "link")
            .FirstOrDefault(x => x.Attribute("rel")?.Value is null or "alternate")
            ?.Attribute("href")?.Value;

    private static DateTimeOffset ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;
}
