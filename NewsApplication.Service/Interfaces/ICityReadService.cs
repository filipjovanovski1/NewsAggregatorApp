using NewsApplication.Domain.DTOs.Scopes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Interfaces
{
    public interface ICityReadService
    {
        Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string query, int limit, CancellationToken ct);
        Task<GeoCandidateDTO?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<GeoCandidateDTO?> FindNearestAsync(double lat, double lng, double maxDistanceKm, CancellationToken ct);
        Task<string?> EnsureLocalNameAsync(GeoCandidateDTO city, CancellationToken ct);

        Task<IReadOnlyList<TopCityDTO>> GetTopByPopulationAsync(string countryIso2, int limit, CancellationToken ct);
    }
}
