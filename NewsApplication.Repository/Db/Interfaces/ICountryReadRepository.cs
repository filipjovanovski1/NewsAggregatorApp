using NewsApplication.Domain.DTOs.Scopes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Repository.Db.Interfaces
{
    public interface ICountryReadRepository
    {
        Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string normalizedToken, int limit, CancellationToken ct);
        Task<GeoCandidateDTO?> GetByIdAsync(string iso2, CancellationToken ct);
    }
}
