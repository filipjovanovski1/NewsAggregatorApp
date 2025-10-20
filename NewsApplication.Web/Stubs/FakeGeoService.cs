using NewsApplication.Domain.DomainModels;
using NewsApplication.Web.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NewsApplication.Web.Stubs
{
    public interface IFakeGeoService
    {
        IEnumerable<SearchResultDto> Search(string query);   // text search like "Skopje" or "MK"
        ReverseResultDto? Reverse(double lat, double lng);   // reverse by lat/lng — centroid-aware
    }

    public sealed class FakeGeoService : IFakeGeoService
    {
        // ---- Data model for the stub ----
        private sealed record CountryCentroid(string Iso2, string Name, double Lat, double Lng);
        private sealed record CityPoint(string Id, string Name, string CountryIso2, double Lat, double Lng);

        // Minimal demo set — add more as you like (ideally load from JSON/ReferenceData)
        private static readonly CountryCentroid[] Countries =
        {
            new("MK","North Macedonia", 41.5956, 21.7167),
            new("DE","Germany",         51.1634, 10.4477),
            new("US","United States",   39.8283, -98.5795),
            new("ES","Spain",           40.4637,  -3.7492),
            new("IT","Italy",           41.8719,  12.5674),
            new("FR","France",          46.2276,   2.2137),
            new("GB","United Kingdom",  55.3781,  -3.4360),
        };

        private static readonly CityPoint[] Cities =
        {
            new("1001","Skopje","MK", 41.9981, 21.4254),
            new("2001","Berlin","DE",  52.5200, 13.4050),
            new("3001","New York","US",40.7128,-74.0060),
            new("4001","Madrid","ES",  40.4168, -3.7038),
            new("5001","Rome","IT",    41.9028, 12.4964),
            new("6001","Paris","FR",   48.8566,  2.3522),
            new("7001","London","GB",  51.5074, -0.1278),
        };

        // ---- Tuning knobs for snapping ----
        private const double CitySnapKm = 60; // prefer a city if within 60 km of the click
        private const int CityMaxChecked = 300;

        // ----------------------------------------------------
        // SEARCH: returns countries (by ISO2/name) and cities (by name/id)
        // ----------------------------------------------------
        public IEnumerable<SearchResultDto> Search(string query)
        {
            query = (query ?? "").Trim().ToLowerInvariant();
            if (query.Length == 0) yield break;

            // Countries: exact ISO2 or name contains
            foreach (var c in Countries)
            {
                if (c.Iso2.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.ToLowerInvariant().Contains(query))
                {
                    yield return new SearchResultDto
                    {
                        Kind = "country",
                        IdOrIso = c.Iso2,
                        Name = c.Name,
                        Lat = c.Lat,      // return centroid so the globe can snap
                        Lng = c.Lng,
                    };
                }
            }

            // Cities: name contains or id matches
            foreach (var city in Cities)
            {
                if (city.Name.ToLowerInvariant().Contains(query) || city.Id == query)
                {
                    yield return new SearchResultDto
                    {
                        Kind = "city",
                        IdOrIso = city.Id,
                        Name = city.Name,
                        Lat = city.Lat,
                        Lng = city.Lng,
                        CountryIso2 = city.CountryIso2
                    };
                }
            }
        }

        // ----------------------------------------------------
        // REVERSE: click on globe → nearest city if close, else nearest country centroid
        // ----------------------------------------------------
        public ReverseResultDto? Reverse(double lat, double lng)
        {
            // 1) Nearest city (quick linear scan; fine for small stub set)
            var nearestCity = Cities
                .Select(c => new { c, dKm = HaversineKm(lat, lng, c.Lat, c.Lng) })
                .OrderBy(x => x.dKm)
                .Take(CityMaxChecked)
                .FirstOrDefault();

            if (nearestCity is not null && nearestCity.dKm <= CitySnapKm)
            {
                var c = nearestCity.c;
                return new ReverseResultDto
                {
                    Kind = "city",
                    IdOrIso = c.Id,
                    Name = c.Name,
                    Lat = c.Lat,   // snap focus to city
                    Lng = c.Lng,
                    CountryIso2 = c.CountryIso2
                };
            }

            // 2) Fallback to nearest country centroid
            var nearestCountry = Countries
                .Select(c => new { c, d2 = Dist2(c.Lat, c.Lng, lat, lng) }) // squared distance is cheaper and fine for comparison
                .OrderBy(x => x.d2)
                .First().c;

            return new ReverseResultDto
            {
                Kind = "country",
                IdOrIso = nearestCountry.Iso2,
                Name = nearestCountry.Name,
                Lat = nearestCountry.Lat,  // snap focus to centroid
                Lng = nearestCountry.Lng,
                CountryIso2 = nearestCountry.Iso2
            };
        }

        // ---- Math helpers ----
        private static double Dist2(double aLat, double aLng, double bLat, double bLng)
            => Math.Pow(aLat - bLat, 2) + Math.Pow(aLng - bLng, 2);

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0; // km
            double dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
    }
}
