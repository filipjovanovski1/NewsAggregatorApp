using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;

namespace NewsApplication.Repository.Db.Implementations.Discovery;

public sealed class NewsSourceRepository : INewsSourceRepository
{
    private readonly ApplicationDbContext _db;

    public NewsSourceRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetKnownDomainsAsync(
        string countryIso2,
        Guid? cityId,
        CancellationToken ct) =>
        await _db.NewsSourceScopes
            .Where(x => x.CountryIso2 == countryIso2 &&
                        x.CityId == cityId &&
                        !x.IsStale &&
                        x.NewsSource!.IsActive)
            .Select(x => x.NewsSource!.Domain)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

    public async Task ImportResultAsync(
        DiscoveryJob job,
        DiscoveryResultDTO result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(result);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var persistedJob = await _db.DiscoveryJobs
            .Include(x => x.DiscoveryTarget)
            .FirstOrDefaultAsync(x => x.Id == job.Id, ct)
            ?? throw new InvalidOperationException($"Discovery job '{job.Id}' was not found.");

        // Pipeline retries are expected. Once a completed import commits, repeating it must be
        // a no-op, including when a shutdown failure callback arrives afterwards.
        if (persistedJob.Status == DiscoveryJobStatus.Completed)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var target = persistedJob.DiscoveryTarget
            ?? throw new InvalidOperationException(
                $"Discovery target '{persistedJob.DiscoveryTargetId}' was not found.");

        var finishedAt = result.FinishedAt ?? DateTimeOffset.UtcNow;
        persistedJob.CompletedAt = finishedAt;
        persistedJob.Warnings = CleanStrings(result.Warnings, lowercase: false);
        persistedJob.Stats = result.Stats;

        if (string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            persistedJob.Status = DiscoveryJobStatus.Failed;
            persistedJob.ErrorStage = result.Error?.Stage;
            persistedJob.ErrorType = result.Error?.Type;
            persistedJob.ErrorMessage = result.Error?.Message;
            ApplyFailureBackoff(target, finishedAt);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }

        if (!string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Unsupported discovery result status '{result.Status}'.");

        ValidateCompletedResult(result, target);

        var normalizedSources = result.Sources
            .Select(x => (Dto: x, Domain: NormalizeDomain(x.Domain)))
            .GroupBy(x => x.Domain, StringComparer.Ordinal)
            .Select(x => x.Last())
            .ToList();

        var domains = normalizedSources.Select(x => x.Domain).ToArray();
        var existingSources = await _db.NewsSources
            .Where(x => domains.Contains(x.Domain))
            .Include(x => x.Feeds)
            .ToDictionaryAsync(x => x.Domain, StringComparer.Ordinal, ct);

        var existingScopes = await _db.NewsSourceScopes
            .Where(x => x.CountryIso2 == target.CountryIso2 && x.CityId == target.CityId)
            .ToListAsync(ct);

        foreach (var incoming in normalizedSources)
        {
            var dto = incoming.Dto;
            if (!existingSources.TryGetValue(incoming.Domain, out var source))
            {
                source = new NewsSource
                {
                    Domain = incoming.Domain,
                    FirstDiscoveredAt = finishedAt,
                    LastDiscoveredAt = finishedAt
                };
                _db.NewsSources.Add(source);
                existingSources.Add(incoming.Domain, source);
            }

            source.LastDiscoveredAt = finishedAt;

            if (dto.SourceFactsRefreshed || source.Name is null)
            {
                source.Name = NullIfBlank(dto.Name);
                source.Url = NullIfBlank(dto.Url);
                source.Language = NullIfBlank(dto.Language)?.ToLowerInvariant();
                source.Classification = dto.Classification;
                source.Confidence = dto.Confidence;
                source.Categories = CleanStrings(dto.Categories, lowercase: true);
                source.IsActive = true;

                UpsertDiscoveryFeeds(source, dto.Feeds);
            }

            var scope = existingScopes.FirstOrDefault(x => x.NewsSourceId == source.Id);
            if (scope is null)
            {
                scope = new NewsSourceScope
                {
                    NewsSourceId = source.Id,
                    CountryIso2 = target.CountryIso2,
                    CityId = target.CityId
                };
                _db.NewsSourceScopes.Add(scope);
                existingScopes.Add(scope);
            }

            scope.Score = dto.Relevance?.Score;
            scope.PollingTier = dto.Relevance?.PollingTier;
            scope.SearchOccurrences = dto.Relevance?.SearchOccurrences;
            scope.MatchedQueries = CleanStrings(
                dto.Relevance?.MatchedQueries ?? [], lowercase: false);
            scope.DiscoveredAt = finishedAt;
            scope.DiscoveryJobId = persistedJob.Id;
            scope.IsStale = false;
        }

        // Absence from the newest completed run is meaningful. Do this in the same transaction
        // as the upserts so readers can never observe new scores alongside old live scopes.
        foreach (var staleScope in existingScopes.Where(x => x.DiscoveryJobId != persistedJob.Id))
            staleScope.IsStale = true;

        persistedJob.Status = DiscoveryJobStatus.Completed;
        persistedJob.ErrorStage = null;
        persistedJob.ErrorType = null;
        persistedJob.ErrorMessage = null;

        target.LastSuccessAt = finishedAt;
        target.ConsecutiveFailures = 0;
        target.ConsecutiveEmptyRuns = result.Sources.Count == 0
            ? target.ConsecutiveEmptyRuns + 1
            : 0;
        target.NextDueAt = finishedAt.AddDays(target.CadenceDays);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UpdateValidatedFeedsAsync(
        IReadOnlyList<FeedValidationResultDTO> results,
        CancellationToken ct)
    {
        if (results.Count == 0)
            return;

        var byId = results
            .GroupBy(x => x.SourceFeedId)
            .ToDictionary(x => x.Key, x => x.Last());
        var ids = byId.Keys.ToArray();

        var feeds = await _db.NewsSourceFeeds
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);

        foreach (var feed in feeds)
        {
            var result = byId[feed.Id];
            feed.IsActive = result.Valid;

            if (!result.Valid)
                continue;

            if (!string.IsNullOrWhiteSpace(result.Url))
                feed.Url = result.Url.Trim();

            feed.Title = NullIfBlank(result.Title);
            feed.EntryCount = result.EntryCount;
            feed.LatestEntry = result.LatestEntry;
            feed.HasFullContent = result.HasFullContent;
            feed.Language = NullIfBlank(result.Language)?.ToLowerInvariant();
            feed.ExternalLinkRatio = result.ExternalLinkRatio;
            feed.DistinctSources = result.DistinctSources;

            // LastPolledAt and LastEtag intentionally remain untouched: they are poller-owned.
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NewsSourceFeed>> GetFeedsForValidationAsync(
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
            return [];

        return await _db.NewsSourceFeeds
            .Where(x => x.NewsSource!.IsActive)
            .Include(x => x.NewsSource)
            .OrderBy(x => x.LatestEntry)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NewsSourceFeed>> GetDueFeedsForPollingAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
            return [];

        // Exact tier cadence is applied in the worker after scopes are loaded. Ten minutes is
        // the shortest tier, so this predicate safely excludes rows that cannot yet be due.
        var earliest = now.AddMinutes(-10);
        return await _db.NewsSourceFeeds
            .Where(x => x.IsActive &&
                        x.NewsSource!.IsActive &&
                        x.NewsSource.Classification == SourceClassifications.NewsSource &&
                        (x.LastPolledAt == null || x.LastPolledAt <= earliest))
            .Include(x => x.NewsSource)
                .ThenInclude(x => x!.Scopes)
            .OrderBy(x => x.LastPolledAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    private static void UpsertDiscoveryFeeds(
        NewsSource source,
        IEnumerable<DiscoveryFeedDTO> incomingFeeds)
    {
        foreach (var dto in incomingFeeds.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
        {
            var url = dto.Url!.Trim();
            var feed = source.Feeds.FirstOrDefault(
                x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));

            if (feed is null)
            {
                feed = new NewsSourceFeed { NewsSourceId = source.Id, Url = url };
                source.Feeds.Add(feed);
            }

            feed.Title = NullIfBlank(dto.Title);
            feed.EntryCount = dto.EntryCount;
            feed.LatestEntry = dto.LatestEntry;
            feed.HasFullContent = dto.HasFullContent;
            feed.Language = NullIfBlank(dto.Language)?.ToLowerInvariant();
            feed.ExternalLinkRatio = dto.ExternalLinkRatio;
            feed.DistinctSources = dto.DistinctSources;
            feed.IsActive = true;
        }
    }

    private static void ValidateCompletedResult(
        DiscoveryResultDTO result,
        DiscoveryTarget target)
    {
        if (!string.Equals(result.Location?.Iso2, target.CountryIso2,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Callback location does not match the job target.");

        Guid? callbackCityId = null;
        if (!string.IsNullOrWhiteSpace(result.Location?.CityId))
        {
            if (!Guid.TryParse(result.Location.CityId, out var parsedCityId))
                throw new InvalidOperationException("Callback city_id is not a valid GUID.");

            callbackCityId = parsedCityId;
        }

        if (callbackCityId != target.CityId)
            throw new InvalidOperationException("Callback city_id does not match the job target.");

        foreach (var source in result.Sources)
        {
            _ = NormalizeDomain(source.Domain);
            if (!SourceClassifications.IsKnown(source.Classification))
                throw new InvalidOperationException(
                    $"Unsupported source classification '{source.Classification}'.");
            if (!PollingTiers.IsKnown(source.Relevance?.PollingTier))
                throw new InvalidOperationException(
                    $"Unsupported polling tier '{source.Relevance?.PollingTier}'.");
            if (source.Confidence is < 0 or > 1)
                throw new InvalidOperationException("Source confidence must be between 0 and 1.");
            if (source.Relevance?.Score is < 0 or > 100)
                throw new InvalidOperationException("Source relevance score must be between 0 and 100.");
        }
    }

    private static void ApplyFailureBackoff(DiscoveryTarget target, DateTimeOffset now)
    {
        target.ConsecutiveFailures++;
        var hours = Math.Min(Math.Pow(2, target.ConsecutiveFailures), 24 * 7);
        target.NextDueAt = now.AddHours(hours);
        if (target.ConsecutiveFailures >= 5)
            target.IsEnabled = false;
    }

    private static string NormalizeDomain(string? value)
    {
        var domain = value?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException("A discovered source has no domain.");
        return domain;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> CleanStrings(IEnumerable<string> values, bool lowercase) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => lowercase ? x.Trim().ToLowerInvariant() : x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
