using NewsApplication.Domain.DTOs.Scopes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Repository.Db.Configurations.ScopeHelpers
{
    internal static class GeoMappers
    {
        public static GeoCandidateDTO ToDTO(this CitySearchRow r) => new()
        {
            Id = r.Id.ToString(),
            Name = r.Name,
            CountryName = r.CountryName,
            CountryIso2 = r.CountryIso2?.ToUpperInvariant(),
            CountryIso3 = r.CountryIso3?.ToUpperInvariant(),   // NEW
            Lat = r.Latitude,
            Lng = r.Longitude,
            Score = r.Score
        };

        public static GeoCandidateDTO ToDTO(this CountrySearchRow r) => new()
        {
            Id = r.CountryIso2?.ToUpperInvariant() ?? "??",
            Name = r.Name,
            CountryName = null,
            CountryIso2 = r.CountryIso2?.ToUpperInvariant(),
            CountryIso3 = r.CountryIso3?.ToUpperInvariant(),   // NEW
            Lat = null,
            Lng = null,
            Score = r.Score
        };
    }
}