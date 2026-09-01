using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Domain.DTOs.Discovery;

namespace NewsApplication.Repository.Db.Interfaces.Discovery;

public interface INewsSourceRepository
{
    Task<IReadOnlyList<string>> GetKnownDomainsAsync(
        string countryIso2,
        Guid? cityId,
        CancellationToken ct);

    Task ImportResultAsync(
        DiscoveryJob job,
        DiscoveryResultDTO result,
        CancellationToken ct);

    Task UpdateValidatedFeedsAsync(
        IReadOnlyList<FeedValidationResultDTO> results,
        CancellationToken ct);

    Task<IReadOnlyList<NewsSourceFeed>> GetFeedsForValidationAsync(
        int limit,
        CancellationToken ct);

    Task<IReadOnlyList<NewsSourceFeed>> GetDueFeedsForPollingAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
