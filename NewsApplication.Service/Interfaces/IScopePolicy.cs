using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Interfaces
{
    public interface IScopePolicy
    {
        bool IsAmbiguous(
            IReadOnlyList<GeoCandidateDTO> countries,
            IReadOnlyList<GeoCandidateDTO> cities,
            IReadOnlyList<string> nonGeoKeywords);

        ScopeKind DecideKind(
            IReadOnlyList<GeoCandidateDTO> countries,
            IReadOnlyList<GeoCandidateDTO> cities);
    }
}
