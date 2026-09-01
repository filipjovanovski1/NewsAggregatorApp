using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Service.Interfaces.Client;

namespace NewsApplication.Service.Interfaces.Discovery;

public interface IDiscoveryJobService
{
    Task<DiscoveryStartResult> StartAsync(DiscoveryTarget target, CancellationToken ct);
}
