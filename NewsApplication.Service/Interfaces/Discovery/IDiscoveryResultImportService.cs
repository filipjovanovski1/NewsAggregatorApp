using NewsApplication.Domain.DTOs.Discovery;

namespace NewsApplication.Service.Interfaces.Discovery;

public enum DiscoveryImportOutcome
{
    Imported,
    AlreadyCompleted,
    NotFound
}

public interface IDiscoveryResultImportService
{
    Task<DiscoveryImportOutcome> ImportAsync(
        Guid jobId,
        DiscoveryResultDTO result,
        CancellationToken ct);
}
