using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes
{
    /*
     ScopeTokenDto is a small data object that represents one parsed piece (“token”) of the user’s search text.
    It stores both the raw text (Raw) and its normalized form (Normalized) so lookups are accent- and 
    case-insensitive.

    !! It records the current "state" of the typed query. !!

    Each token knows whether it could represent a geographic entity (IsGeoCandidate), what type it matched 
    (MatchedEntityType = "city" or "country"), the matched entity ID, its country code (for city tokens), 
    and the similarity score from DB lookups.

    In practice, it’s the parser’s atomic unit — used for:

    *   Explaining why certain words became geo “pills” in the UI,

    *   Caching and reusing lookup results by Normalized,

    *   Detecting city/country patterns or ambiguities, and

    *   Preventing redundant DB/API calls during incremental search.
    
     */
    public sealed class ScopeTokenDTO
    {
        public string Raw { get; init; } = string.Empty;

        // Normalized : lower + unaccent - removes diacritics
        public string Normalized { get; init; } = string.Empty;
        public string MatchedEntityType { get; init; } = "non-geo";

        public IReadOnlyList<GeoCandidateDTO> Countries { get; init; } = [];
        public IReadOnlyList<GeoCandidateDTO> Cities { get; init; } = [];
    }
}
