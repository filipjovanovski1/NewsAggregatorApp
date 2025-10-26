using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes
{
    /*
        GeoCandidateDto represents a single, UI-ready geo suggestion produced by the database lookup 
        for a user token. 
        
        Each instance corresponds to either a country or a city and includes the 
        entity’s Id and display Name; for cities it also carries CountryName and CountryIso2 so the UI 
        can label results (“San José, CR”), group pills by country, and color clusters consistently. 
        
        Lat/Lng are included so markers can be plotted immediately without additional API calls. 
        
        Score preserves the similarity/ranking from the lookup, allowing you to sort candidates 
        and hide weak matches. 
        
        In practice, these objects are what the user clicks: they drive pill rendering, 
        map seeding, and disambiguation. They intentionally decouple parsing (ScopeTokenDto says “this looks 
        like a place”) from presentation (“here are the concrete places to choose”), and they cache well by 
        normalized token to keep the UI fast and stable.
     */
    public sealed class GeoCandidateDTO
    {
        public string Id { get; init; } = default!;           // Country.Id or City.Id
        public string Name { get; init; } = default!;
        public string? CountryName { get; init; } // for cities
        public string? CountryIso2 { get; init; }
        public string? CountryIso3 { get; set; }
        public double? Lat { get; init; }
        public double? Lng { get; init; }
        public double Score { get; init; }
    }
}
