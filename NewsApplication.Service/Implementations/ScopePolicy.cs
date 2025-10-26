using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Domain.Enums;
using NewsApplication.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Implementations
{
    public sealed class ScopePolicy : IScopePolicy
    {
        public static readonly string PolicyVersion = "ScopePolicy v2 (score-aware + tokenizer)";

        private readonly IQueryTokenizer _tokenizer;
        public ScopePolicy(IQueryTokenizer tokenizer) 
        { 
            _tokenizer = tokenizer;
            Console.WriteLine($"[ScopePolicy] Loaded {PolicyVersion}");
        }
        public string? DebugChooseCountryIso2(
            IReadOnlyList<GeoCandidateDTO> countries,
            IReadOnlyList<string> nonGeoKeywords,
            Action<string, string>? trace = null)
        {
            return ChooseCountryIso2(countries, nonGeoKeywords, trace);
        }


        private string? ChooseCountryIso2(
        IReadOnlyList<GeoCandidateDTO> countries,
        IReadOnlyList<string> nonGeoKeywords,
        Action<string, string>? trace = null)
        {
            if (countries == null || countries.Count == 0) return null;

            var ordered = countries
            .OrderByDescending(c => c.Score)
            .ThenBy(c => _tokenizer.Normalize(c.Name ?? string.Empty))
            .ToList();

            var top = ordered[0];
            var topIso2 = top.CountryIso2?.ToUpperInvariant();

            var second = ordered.Skip(1).Select(c => c.Score).DefaultIfEmpty(0).First();
            var topScore = top.Score;

            trace?.Invoke("chooseCountry.top",
               $"iso={topIso2} score={topScore:F3} second={second:F3} count={countries.Count}");

            // clear leader → choose
            if (!string.IsNullOrWhiteSpace(topIso2) && 
                (countries.Count == 1 || (topScore - second) >= 0.10 || topScore >= 0.90))
                return topIso2;

            var kw = new HashSet<string>(
            (nonGeoKeywords ?? Array.Empty<string>())
                .Select(t => _tokenizer.Normalize(t))
                .Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.Ordinal
            );

            foreach (var co in ordered)
            {
                var iso2 = co.CountryIso2?.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(iso2)) continue;

                var tokens = _tokenizer.Split(co.Name ?? string.Empty)
                               .Select(t => _tokenizer.Normalize(t))
                               .Where(t => !string.IsNullOrWhiteSpace(t))
                               .ToArray();

                if (tokens.Length > 0 && tokens.All(kw.Contains))
                {
                    trace?.Invoke("chooseCountry.phrase", $"picked={iso2} name='{co.Name}' tokens=[{string.Join(",", tokens)}]");
                    return iso2;
                }
            }
            trace?.Invoke("chooseCountry.none", "no clear leader and no phrase match");
            return null;
        }

        public ScopeKind DecideKind(
         IReadOnlyList<GeoCandidateDTO> countries,
         IReadOnlyList<GeoCandidateDTO> cities)
        {
            // No geo at all
            if ((countries == null || countries.Count == 0) &&
                (cities == null || cities.Count == 0))
                return ScopeKind.Other;

            // Pure country case (0 cities)
            if (cities == null || cities.Count == 0)
            {
                if (countries?.Count <= 0) return ScopeKind.Other;

                // If we can effectively choose one country → Country; else Composite
                var chosenIso2 = ChooseCountryIso2(countries, Array.Empty<string>());
                if (!string.IsNullOrWhiteSpace(chosenIso2))
                    return ScopeKind.Country;

                return countries!.Count >= 2 ? ScopeKind.Composite : ScopeKind.Country;
            }

            // There are cities. Prefer deciding relative to a chosen country.
            var chosen = ChooseCountryIso2(countries ?? Array.Empty<GeoCandidateDTO>(), Array.Empty<string>());

            if (!string.IsNullOrWhiteSpace(chosen))
            {
                var inCountryDistinctIds = cities
                    .Where(c => string.Equals(c.CountryIso2, chosen, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct()
                    .Count();

                if (inCountryDistinctIds == 1) return ScopeKind.City;            // ← “paris france”
                if (inCountryDistinctIds >= 2) return ScopeKind.CityInCountry;   // ← “san jose costa rica”

                // Chosen country has no cities (all cities belong elsewhere) → Composite
                return ScopeKind.Composite;
            }

            // No chosen country: do cities span multiple countries?
            var distinctCos = cities.Select(c => c.CountryIso2)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Count();

            if (distinctCos >= 2) return ScopeKind.Composite;

            // No chosen country but multiple cities all within one country → CityInCountry
            if (distinctCos == 1 && cities.Select(c => c.Id).Distinct().Count() > 1)
                return ScopeKind.CityInCountry;

            // Otherwise treat as a single city scope
            return ScopeKind.City;
        }



        public bool IsAmbiguous(
         IReadOnlyList<GeoCandidateDTO> countries,
         IReadOnlyList<GeoCandidateDTO> cities,
         IReadOnlyList<string> nonGeoKeywords)
        {
            Console.WriteLine($"[ScopePolicy] Start IsAmbiguous. Cities={cities?.Count}, Countries={countries?.Count}");

            if (cities == null || cities.Count == 0) return false;

            // Detect same-country, multiple-city case early (no chosen country)
            if (countries.Count == 0 &&
                cities.GroupBy(c => c.CountryIso2)
                      .Any(g => g.Count() > 1 && g.Select(x => x.Id).Distinct().Count() > 1))
            {
                Console.WriteLine("[ScopePolicy] same-country multi-city ambiguity detected (no chosen country)");
                return true;
            }

            var chosenIso2 = ChooseCountryIso2(countries, nonGeoKeywords,
                (k, v) => Console.WriteLine($"[ScopePolicy] {k} {v}"));

            Console.WriteLine($"[ScopePolicy] chosenIso2={chosenIso2 ?? "(none)"}");

            // In-country tie at the top → ambiguous
            if (!string.IsNullOrWhiteSpace(chosenIso2))
            {
                var inCountry = cities
                    .Where(c => string.Equals(c.CountryIso2, chosenIso2, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[ScopePolicy] inCountryCount={inCountry.Count}");

                if (inCountry.Count == 0)
                {
                    // NEW #1: "country–city conflict" → ambiguous
                    // If we effectively chose a country, but none of the city candidates belong to it,
                    // and there exists a high-confidence city elsewhere, treat as ambiguous.
                    var topCityScore = cities.Max(c => c.Score);
                    const double hi = 0.90;       // same high floor you already use
                    const double eps = 0.01;      // within 1% of top

                    var hasStrongCityElsewhere = cities.Any(c =>
                        !string.Equals(c.CountryIso2, chosenIso2, StringComparison.OrdinalIgnoreCase) &&
                        c.Score >= Math.Max(hi, topCityScore - eps));

                    if (hasStrongCityElsewhere)
                        return true;

                    // If no strong city elsewhere, fall back to your cross-country tie logic
                    chosenIso2 = null;
                }
                if (inCountry.Count > 1)
                {
                    var top = inCountry.Max(c => c.Score);
                    const double eps = 0.01; // within 1% of top
                    const double hi = 0.90;

                    var tiedDistinct = inCountry
                        .Where(c => (c.Score) >= Math.Max(hi, top - eps))
                        .Select(c => c.Id)
                        .Distinct()
                        .Count();

                    Console.WriteLine($"[ScopePolicy] inCountryTop={top:F3} tiedDistinct={tiedDistinct}");

                    if (tiedDistinct > 1) return true;
                }

                return false; // one clear city in the chosen country
            }
            // --- NEW: Detect multiple same-country cities (e.g., many Springfields in US) ---
            if (cities.GroupBy(c => c.CountryIso2)
                      .Any(g => g.Count() > 1 && g.Select(x => x.Id).Distinct().Count() > 1))
            {
                Console.WriteLine("[ScopePolicy] same-country multi-city ambiguity detected");
                return true;
            }
            // No chosen country → cross-country top near-tie → ambiguous
            {
                var top = cities.Max(c => c.Score);
                const double eps = 0.05;
                const double hi = 0.90;

                var topCountryCount = cities
                    .Where(c => (c.Score) >= Math.Max(hi, top - eps))
                    .Select(c => c.CountryIso2?.ToUpperInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .Count();

                Console.WriteLine($"[ScopePolicy] crossCountryTop={top:F3} distinctTopCountries={topCountryCount}");

                if (topCountryCount >= 2) return true;
            }

            return false;
        }

    }
}
