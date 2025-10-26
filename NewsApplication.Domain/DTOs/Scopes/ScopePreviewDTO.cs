using NewsApplication.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes
{
    /*
     
     ScopePreviewDto is the pre-commit preview of a user’s geo intent—used to render country/city pills, 
    show map markers, and decide if search is allowed before calling any API. It carries your best-guess 
    Kind (City, Country, CityInCountry, Composite, Other), an ambiguity flag IsAmbiguous, and a structural 
    guard IsBlocking. 
    
    Keep in mind: ambiguity blocks via IsAmbiguous, while policy/structure blocks via 
    IsBlocking (e.g., CityInCountry always blocks until a specific city is chosen; Composite blocks only 
    while ambiguous; City/Country/Other don’t add extra blocking). The UI typically binds to 
    CanSearch = !IsAmbiguous && !IsBlocking. Tokens explain the parse; CountryMatches, CityMatches, and 
    CitiesGroupedByCountry power pills and markers; NonGeoKeywords (like “sports”) are preserved and appended 
    once the scope is resolved. Only when the preview is unambiguous (and not structurally blocked) should you 
    construct the final scope and hit the news API.

    IsAmbiguous answers: “Are we still unsure what place the user means?”

    IsBlocking answers: “Even if we weren’t ambiguous, is this scope structurally disallowed right now?” 
    (e.g., CityInCountry needs a specific city; Composite can only run when unambiguous.)

    OriginalQuery — The exact text the user typed (echo in UI, keep for telemetry).

    Tokens (List<ScopeTokenDTO>) — Per-chunk parse results (geo vs non-geo, matches, scores) used to 
    explain and drive pills.

    CountryMatches (List<GeoCandidateDTO>) — Clickable country suggestions inferred from the query.

    CityMatches (List<GeoCandidateDTO>) — Flat list of city suggestions across all countries.

    CitiesGroupedByCountry (Dictionary<string, List<GeoCandidateDTO>>) — City suggestions organized by 
    CountryIso2 for grouped pills/map clusters.

    NonGeoKeywords (List<string>) — Preserved non-geo terms (e.g., “sports”) appended to the final search.

    Targets (List<GeoCandidateDTO>) — Resolved, executable targets (single or allowed composite) once the 
    preview is unambiguous.

    */
    public class ScopePreviewDTO
    {
        public ScopeKind Kind { get; init; } = ScopeKind.Other;

        public bool IsAmbiguous { get; init; }

        public bool IsBlocking =>
            Kind switch
            {
                ScopeKind.Composite => IsAmbiguous,
                ScopeKind.CityInCountry => true,
                _ => false
            };

        public Dictionary<string, object?>? Diagnostics { get; init; }
        public bool CanSearch => !IsAmbiguous && !IsBlocking;

        public string OriginalQuery { get; init; } = string.Empty;

        public List<ScopeTokenDTO> Tokens { get; init; } =
            new List<ScopeTokenDTO>();

        public List<GeoCandidateDTO> CountryMatches { get; init; } =
            new List<GeoCandidateDTO>();

        public List<GeoCandidateDTO> CityMatches { get; init; } =
            new List<GeoCandidateDTO>();

        public Dictionary<string, List<GeoCandidateDTO>> CitiesGroupedByCountry { get; init; } =
            new Dictionary<string, List<GeoCandidateDTO>>();

        public List<string> NonGeoKeywords { get; init; } =
            new List<string>();

        public List<GeoCandidateDTO> Targets { get; init; } =
            new List<GeoCandidateDTO>();


    }


}
