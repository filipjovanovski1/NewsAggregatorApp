using NewsApplication.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes
{
    /*
    Represents the final, fully resolved search scope that signals it is safe to fetch news.

    It captures a concrete and unambiguous geographic target — such as a specific city, country, 
    or an allowed composite of multiple locations — along with stable identifiers, ISO codes, and 
    display names for consistent reference and logging.

    Latitude and longitude provide a central coordinate for map centering or geo-biased searches.
    ExtraKeywords preserve the user’s non-geographic intent (like “sports” or “tech”) to maintain 
    context across UI previews and API calls.

    The CanonicalQueryForNews property generates a single deterministic, human-readable string that 
    acts as both the query sent to the news provider and a stable cache key, ensuring identical scopes 
    with the same filters always yield the same results.
     */
    public sealed class FinalScopeDTO
    {
        public ScopeKind Kind { get; init; }            // City | CityInCountry | Country | Other | Composite
        public Guid? CountryId { get; init; }
        public Guid? CityId { get; init; }
        public string CountryIso2 { get; init; } = "";
        public string CityName { get; init; } = "";
        public double? Lat { get; init; }
        public double? Lng { get; init; }
        public List<string> ExtraKeywords { get; init; } = new();
        public string CanonicalQueryForNews { get; init; } = "";
    }

}
