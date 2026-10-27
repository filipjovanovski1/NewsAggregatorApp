using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Domain.DTOs.Ingestion;
using NewsApplication.Repository.Db.Interfaces;

namespace NewsApplication.Repository.Db.Implementations;

public sealed class FeedIngestionRepository : IFeedIngestionRepository
{
    private readonly ApplicationDbContext _db;

    public FeedIngestionRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<FeedClaimDTO>> ClaimAsync(
        FeedClaimRequestDTO request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var limit = Math.Clamp(request.MaxItems, 1, 100);
        var leaseSeconds = Math.Clamp(request.LeaseSeconds, 30, 1800);
        var owner = request.WorkerId.Trim();
        if (owner.Length is 0 or > 256)
            throw new ArgumentException("workerId must contain between 1 and 256 characters.");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var ids = await _db.Database.SqlQuery<Guid>($"""
            SELECT f."Id" AS "Value"
            FROM "NewsSourceFeeds" AS f
            INNER JOIN "NewsSources" AS s ON s."Id" = f."NewsSourceId"
            WHERE f."IsActive" = TRUE
              AND s."IsActive" = TRUE
              AND s."Classification" = {SourceClassifications.NewsSource}
              AND (f."NextPollAt" IS NULL OR f."NextPollAt" <= {now})
              AND (f."LeaseExpiresAt" IS NULL OR f."LeaseExpiresAt" <= {now})
              AND EXISTS (
                  SELECT 1 FROM "NewsSourceScopes" AS scope
                  WHERE scope."NewsSourceId" = s."Id" AND scope."IsStale" = FALSE
              )
            ORDER BY f."NextPollAt" NULLS FIRST, f."LastPolledAt" NULLS FIRST
            LIMIT {limit}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);

        if (ids.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return [];
        }

        var feeds = await _db.NewsSourceFeeds
            .Where(x => ids.Contains(x.Id))
            .Include(x => x.NewsSource)
                .ThenInclude(x => x!.Scopes)
            .ToListAsync(ct);

        var claimed = new List<FeedClaimDTO>(feeds.Count);
        foreach (var feed in feeds.OrderBy(x => ids.IndexOf(x.Id)))
        {
            var leaseId = Guid.NewGuid();
            var expiresAt = now.AddSeconds(leaseSeconds);
            feed.LeaseId = leaseId;
            feed.LeaseOwner = owner;
            feed.LeaseVersion++;
            feed.LeaseExpiresAt = expiresAt;

            var source = feed.NewsSource!;
            claimed.Add(new FeedClaimDTO(
                leaseId,
                feed.LeaseVersion,
                expiresAt,
                feed.Id,
                feed.ResolvedUrl ?? feed.Url,
                source.Id,
                source.Domain,
                source.Name,
                feed.Language ?? source.Language,
                feed.LastEtag,
                feed.LastModified,
                feed.LastPolledAt,
                PollingTier(source.Scopes)));
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return claimed;
    }

    public async Task<FeedIngestionWriteOutcome> CompleteAsync(
        Guid leaseId,
        string idempotencyKey,
        CompleteFeedPollDTO request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var feed = await _db.NewsSourceFeeds
            .Include(x => x.NewsSource)
                .ThenInclude(x => x!.Scopes)
            .FirstOrDefaultAsync(
                x => x.LeaseId == leaseId || x.LastCompletionLeaseId == leaseId,
                ct);

        if (feed is null)
            return FeedIngestionWriteOutcome.NotFound;
        if (feed.LastCompletionLeaseId == leaseId && feed.LastCompletionKey == idempotencyKey)
            return FeedIngestionWriteOutcome.AlreadyApplied;
        if (feed.LeaseId != leaseId ||
            feed.LeaseVersion != request.LeaseVersion ||
            feed.LeaseExpiresAt <= now)
            return FeedIngestionWriteOutcome.StaleLease;

        var articlesByHash = new Dictionary<string, Article>(StringComparer.Ordinal);
        var prepared = request.Articles
            .Where(IsValidArticle)
            .Select(x => (Payload: x, UrlHash: Hash(x.CanonicalUrl), EntryHash: Hash(x.EntryUrl)))
            .GroupBy(x => x.UrlHash, StringComparer.Ordinal)
            .Select(x => x.Last())
            .ToList();
        var urlHashes = prepared.Select(x => x.UrlHash).ToArray();

        if (urlHashes.Length > 0)
        {
            articlesByHash = await _db.Articles
                .Where(x => x.CanonicalUrlHash != null && urlHashes.Contains(x.CanonicalUrlHash))
                .ToDictionaryAsync(x => x.CanonicalUrlHash!, StringComparer.Ordinal, ct);
        }

        var externalIds = prepared.Select(x => x.Payload.ExternalEntryId).Distinct().ToArray();
        var entryHashes = prepared.Select(x => x.EntryHash).Distinct().ToArray();
        var existingEntries = externalIds.Length == 0
            ? new List<ArticleFeedEntry>()
            : await _db.ArticleFeedEntries
                .Where(x => x.NewsSourceFeedId == feed.Id &&
                            (externalIds.Contains(x.ExternalEntryId) ||
                             entryHashes.Contains(x.EntryUrlHash)))
                .ToListAsync(ct);
        var entriesById = existingEntries
            .GroupBy(x => x.ExternalEntryId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var entriesByUrl = existingEntries
            .GroupBy(x => x.EntryUrlHash, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var item in prepared)
        {
            if (!articlesByHash.TryGetValue(item.UrlHash, out var article))
            {
                article = new Article
                {
                    Provider = "RSS",
                    ProviderArticleId = item.UrlHash,
                    Title = item.Payload.Title.Trim(),
                    Publisher = feed.NewsSource!.Name ?? feed.NewsSource.Domain,
                    Url = item.Payload.CanonicalUrl.Trim(),
                    PublishedTime = item.Payload.PublishedAt.UtcDateTime
                };
                articlesByHash.Add(item.UrlHash, article);
                _db.Articles.Add(article);
            }

            ApplyArticle(article, item.Payload, item.UrlHash, now);

            if (!entriesById.TryGetValue(item.Payload.ExternalEntryId, out var entry) &&
                !entriesByUrl.TryGetValue(item.EntryHash, out entry))
            {
                entry = new ArticleFeedEntry
                {
                    NewsSourceFeedId = feed.Id,
                    ArticleId = article.Id,
                    ExternalEntryId = item.Payload.ExternalEntryId.Trim(),
                    EntryUrl = item.Payload.EntryUrl.Trim(),
                    EntryUrlHash = item.EntryHash,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };
                entriesById.Add(entry.ExternalEntryId, entry);
                entriesByUrl.Add(entry.EntryUrlHash, entry);
                _db.ArticleFeedEntries.Add(entry);
            }
            else
            {
                entry.ArticleId = article.Id;
                entry.EntryUrl = item.Payload.EntryUrl.Trim();
                entry.EntryUrlHash = item.EntryHash;
                entry.LastSeenAt = now;
            }
            entry.FeedPublishedAt = item.Payload.PublishedAt;
        }

        if (ValidHttpUrl(request.Feed.FinalUrl))
            feed.ResolvedUrl = request.Feed.FinalUrl.Trim();
        feed.LastEtag = NullIfBlank(request.Feed.Etag);
        feed.LastModified = NullIfBlank(request.Feed.LastModified);
        feed.LastPolledAt = request.Feed.PolledAt;
        feed.LastSuccessfulPollAt = request.Feed.PolledAt;
        feed.LastHttpStatus = request.Feed.HttpStatus;
        feed.ConsecutiveFailures = 0;
        feed.LastErrorType = null;
        feed.LastErrorMessage = null;
        feed.NextPollAt = request.Feed.PolledAt.Add(PollingInterval(feed.NewsSource!.Scopes));
        feed.LastCompletionLeaseId = leaseId;
        feed.LastCompletionKey = idempotencyKey;
        ClearLease(feed);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return FeedIngestionWriteOutcome.Applied;
    }

    public async Task<FeedIngestionWriteOutcome> FailAsync(
        Guid leaseId,
        FailFeedPollDTO request,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var feed = await _db.NewsSourceFeeds.FirstOrDefaultAsync(x => x.LeaseId == leaseId, ct);
        if (feed is null)
            return FeedIngestionWriteOutcome.NotFound;
        if (feed.LeaseVersion != request.LeaseVersion || feed.LeaseExpiresAt <= now)
            return FeedIngestionWriteOutcome.StaleLease;

        feed.LastPolledAt = request.FailedAt;
        feed.LastHttpStatus = request.HttpStatus;
        feed.LastErrorType = request.ErrorType[..Math.Min(request.ErrorType.Length, 128)];
        feed.LastErrorMessage = request.Message[..Math.Min(request.Message.Length, 2000)];
        feed.ConsecutiveFailures++;

        var retrySeconds = request.RetryAfterSeconds is > 0
            ? Math.Min(request.RetryAfterSeconds.Value, 24 * 60 * 60)
            : Math.Min(300 * Math.Pow(2, feed.ConsecutiveFailures - 1), 24 * 60 * 60);
        feed.NextPollAt = request.FailedAt.AddSeconds(retrySeconds);
        ClearLease(feed);

        await _db.SaveChangesAsync(ct);
        return FeedIngestionWriteOutcome.Applied;
    }

    private static void ApplyArticle(
        Article article,
        IngestedArticleDTO payload,
        string urlHash,
        DateTimeOffset now)
    {
        article.Title = payload.Title.Trim();
        article.Description = NullIfBlank(payload.Description);
        article.ContentText = NullIfBlank(payload.ContentText);
        article.ContentHash = NullIfBlank(payload.ContentHash);
        article.Author = NullIfBlank(payload.Author);
        article.Language = NullIfBlank(payload.Language)?.ToLowerInvariant();
        article.ImageUrl = NullIfBlank(payload.ImageUrl);
        article.CanonicalUrl = payload.CanonicalUrl.Trim();
        article.CanonicalUrlHash = urlHash;
        article.Url = payload.CanonicalUrl.Trim();
        article.PublishedTime = payload.PublishedAt.UtcDateTime;
        article.ModifiedTime = payload.ModifiedAt?.UtcDateTime;
        article.Categories = payload.Categories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        article.ExtractedAt = now;
        article.ExtractionStatus = payload.ExtractionStatus;
        article.Extractor = NullIfBlank(payload.Extractor);
        article.ExtractorVersion = NullIfBlank(payload.ExtractorVersion);
    }

    private static bool IsValidArticle(IngestedArticleDTO article) =>
        !string.IsNullOrWhiteSpace(article.ExternalEntryId) &&
        article.ExternalEntryId.Length <= 2048 &&
        !string.IsNullOrWhiteSpace(article.Title) &&
        ValidHttpUrl(article.EntryUrl) &&
        ValidHttpUrl(article.CanonicalUrl);

    private static void ClearLease(NewsSourceFeed feed)
    {
        feed.LeaseId = null;
        feed.LeaseOwner = null;
        feed.LeaseExpiresAt = null;
    }

    private static string PollingTier(IEnumerable<NewsSourceScope> scopes) =>
        scopes.Where(x => !x.IsStale)
            .Select(x => x.PollingTier)
            .OrderBy(TierRank)
            .FirstOrDefault() ?? PollingTiers.Low;

    private static TimeSpan PollingInterval(IEnumerable<NewsSourceScope> scopes) =>
        PollingTier(scopes) switch
        {
            PollingTiers.High => TimeSpan.FromMinutes(15),
            PollingTiers.Medium => TimeSpan.FromHours(1),
            PollingTiers.Low => TimeSpan.FromHours(6),
            _ => TimeSpan.FromDays(1)
        };

    private static int TierRank(string? tier) => tier switch
    {
        PollingTiers.High => 0,
        PollingTiers.Medium => 1,
        PollingTiers.Low => 2,
        _ => 3
    };

    private static bool ValidHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())))
            .ToLowerInvariant();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
