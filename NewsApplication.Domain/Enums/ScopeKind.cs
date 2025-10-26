using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.Enums
{
    // None - When the parsed query has no usable tokens (empty, only punctuation/stop-words)
    // or the user just focused the box.
    //
    // City - The user has selected a specific city instance (a concrete CityId in the DB).
    // The city name is unique enough in your DB that the preview returned only one option.
    //
    // Country - The query unambiguously resolves to a single country (e.g., “Costa Rica”, “CR”).
    //
    // CityInCountry - The query constrains the country but there are multiple cities with the
    // same name inside that country—so still ambiguous. Tells the UI to place multiple markers
    // within a single country and prevent token spend. Keeps the non-geo keywords(e.g., “sports”)
    // parked until the user picks the exact city.
    //
    // Other - No geo tokens at all (e.g., “sports”, “macroeconomy”, “OpenAI”).Persist keywords
    // so that when a place gets chosen later you can build “City Country keywords”.
    //
    // Composite - The query contains multiple geo scopes at once or a mix that can’t be reduced
    // to one scope. Still needed for football matches or other events that span multiple places.  
    public enum ScopeKind { None = 0, City = 1, Country = 2, CityInCountry = 3, Other = 4, Composite = 5 }
}
