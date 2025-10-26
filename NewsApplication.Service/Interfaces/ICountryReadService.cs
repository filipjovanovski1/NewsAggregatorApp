using NewsApplication.Domain.DTOs.Scopes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Interfaces
{
    public interface ICountryReadService
    {
        Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string query, int limit, CancellationToken ct);
        Task<GeoCandidateDTO?> GetByIdAsync(string iso2, CancellationToken ct);
    }
}
