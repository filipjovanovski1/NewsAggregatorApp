using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static NewsApplication.Web.Controllers.ScopeController;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("scope")]
public sealed class ScopeController : ControllerBase
{
    public sealed class ResolveScopeRequest
    {
        public string? Q { get; set; }                 // free text (from searchbar)
        public CityPick? City { get; set; }            // chosen city (from pill or globe)
        public CountryPick? Country { get; set; }      // chosen country (from pill)
    }

    public sealed class CityPick
    {
        public Guid Id { get; set; }                    // City PK (optional for lookup)
        public string Name { get; set; } = default!;
        public string CountryIso2 { get; set; } = default!;
    }

    public sealed class CountryPick
    {
        public string Iso2 { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Iso3 { get; set; }
    }

    public sealed class ReverseScopeRequest
    {
        public double? Lat { get; set; }
        public double? Lng { get; set; }
    }

    public sealed class ResolveScopeResponse
    {
        public string ScopeKey { get; set; } = default!;
        public string Kind { get; set; } = default!;
        public string Label { get; set; } = default!;
        public string? CountryIso2 { get; set; }
        public string? CountryIso3 { get; set; }
        public string? CityId { get; set; }
        public double? FocusLat { get; set; }
        public double? FocusLng { get; set; }
    }

    private const double CitySnapDistanceKm = 60d;

    // -------------------------------------------
    // Helpers: slugify & scope key formatting
    // -------------------------------------------

    private static string Slugify(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        s = s.Trim().ToLowerInvariant();

        // Basic ASCII fold (remove diacritics)
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(capacity: normalized.Length);
        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var ascii = sb.ToString().Normalize(NormalizationForm.FormC);

        // keep letters/numbers, turn spaces and separators into hyphens
        ascii = Regex.Replace(ascii, @"[^a-z0-9\s\-]", "");
        ascii = Regex.Replace(ascii, @"[\s\-_]+", "-").Trim('-');
        return ascii;
    }

    private static string CityScopeKey(string? cityName, string? countryIso2, string? localName, string? query)
    {
        var isoLower = (countryIso2 ?? string.Empty).Trim().ToLowerInvariant();
        var slugRoot = Slugify(cityName ?? string.Empty);
        var slug = string.IsNullOrWhiteSpace(slugRoot)
            ? isoLower
            : string.IsNullOrWhiteSpace(isoLower) ? slugRoot : $"{slugRoot}-{isoLower}";

        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(slug))
            segments.Add($"city:{slug}");

        var isoUpper = (countryIso2 ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(isoUpper))
            segments.Add($"country:{isoUpper}");

        if (!string.IsNullOrWhiteSpace(localName))
        {
            var encoded = Uri.EscapeDataString(localName.Trim());
            segments.Add($"local:{encoded}");
        }

        if (!string.IsNullOrWhiteSpace(query))
            segments.Add($"q:{query.Trim()}");

        return string.Join('|', segments);
    }

    private static string CountryScopeKey(string? iso2, string? query)
    {
        var segments = new List<string>();
        var code = (iso2 ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(code))
            segments.Add($"country:{code}");

        if (!string.IsNullOrWhiteSpace(query))
            segments.Add($"q:{query.Trim()}");

        return string.Join('|', segments);
    }

    private static string BuildCityLabel(string? name, string? countryIso2)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var iso = string.IsNullOrWhiteSpace(countryIso2) ? null : countryIso2.Trim().ToUpperInvariant();
        if (trimmedName is null && iso is null) return string.Empty;
        if (trimmedName is null) return iso!;
        return iso is null ? trimmedName : $"{trimmedName}, {iso}";
    }
    // Remove country mentions from q when we already have ISO2 (generic for any country)
    private static string? SanitizeQueryForCountry(string? q, string? iso2, string? countryName, string? iso3 = null)
    {
        if (string.IsNullOrWhiteSpace(q)) return null;

        string norm(string s) =>
            Regex.Replace(s.Normalize(NormalizationForm.FormD), @"\p{Mn}", "")
                 .Normalize(NormalizationForm.FormC)
                 .ToLowerInvariant();

        var kill = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(iso2)) kill.Add(norm(iso2!));
        if (!string.IsNullOrWhiteSpace(iso3)) kill.Add(norm(iso3!));
        if (!string.IsNullOrWhiteSpace(countryName)) kill.Add(norm(countryName!));

        // token filter: drop exact matches to iso2/iso3/country name token(s)
        var tokens = Regex.Split(q, @"\s+")
                          .Select(t => t.Trim())
                          .Where(t => t.Length > 0)
                          .ToList();

        var kept = tokens.Where(t => !kill.Contains(norm(t))).ToList();

        // If user typed "Skopje North Macedonia", kill full-name tokens too:
        var joined = string.Join(" ", kept);
        if (!string.IsNullOrWhiteSpace(countryName))
        {
            var cn = norm(countryName!);
            // remove full phrase occurrences loosely
            joined = Regex.Replace(joined, $@"\b{Regex.Escape(cn)}\b", "", RegexOptions.IgnoreCase).Trim();
        }

        return string.Join(" ", Regex.Split(joined, @"\s+").Where(s => s.Length > 0));
    }




    // -------------------------------------------
    // Resolve with combined inputs (city/country + q)
    // -------------------------------------------

    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveScopeResponse>> Resolve(
        [FromBody] ResolveScopeRequest req,
        [FromServices] IScopeResolverService resolver,
        [FromServices] ICityReadService cityService,
        [FromServices] ICountryReadService countryService,
        CancellationToken ct)
    {
        // 1) Explicit city pick (with optional Q)
        if (req.City is not null)
        {
            // Prefer DB row if Id provided
            var city = req.City.Id != Guid.Empty
                ? await cityService.GetByIdAsync(req.City.Id, ct)
                : null;

            var cityName = city?.Name ?? req.City.Name;
            var iso2 = (city?.CountryIso2 ?? req.City.CountryIso2 ?? "").ToUpperInvariant();
            var localName = city?.LocalName;
            if (city is not null && string.IsNullOrWhiteSpace(localName))
            {
                localName = await cityService.EnsureLocalNameAsync(city, ct);
            }

            // fetch country dto once (for label + sanitizing)
            var countryDto = string.IsNullOrWhiteSpace(iso2) ? null : await countryService.GetByIdAsync(iso2, ct);

            // sanitize q against country
            
            var cleanQ = SanitizeQueryForCountry(req.Q, iso2, countryDto?.Name, countryDto?.CountryIso3);
            // If user provided no query, fall back to the city name so both Latin/local forms can be OR-ed downstream
            if (string.IsNullOrWhiteSpace(cleanQ))
            {
                cleanQ = cityName;
            }
            var scopeKey = CityScopeKey(cityName, iso2, localName, cleanQ);

            var cityIdStr = !string.IsNullOrWhiteSpace(city?.Id) ? city!.Id : null;

            return Ok(new ResolveScopeResponse
            {
                ScopeKey = scopeKey,
                Kind = "city",
                Label = await BuildCityLabelWithCountryNameAsync(countryService, cityName, iso2, ct),
                CountryIso2 = iso2,
                CountryIso3 = (city?.CountryIso3 ?? "").ToUpperInvariant(),
                CityId = cityIdStr ?? (req.City.Id != Guid.Empty ? req.City.Id.ToString() : null),
                FocusLat = city?.Lat,
                FocusLng = city?.Lng
            });

        }

        // 2) Explicit country pick (with optional Q)
        if (req.Country is not null)
        {
            var iso2 = (req.Country.Iso2 ?? "").ToUpperInvariant();
            var country = !string.IsNullOrWhiteSpace(iso2)
                ? await countryService.GetByIdAsync(iso2, ct)
                : null;

            var cleanQ = SanitizeQueryForCountry(req.Q, iso2, country?.Name, country?.CountryIso3);
            var scopeKey = CountryScopeKey(iso2, cleanQ);

            return Ok(new ResolveScopeResponse
            {
                ScopeKey = scopeKey,        // e.g., country:CA|q:sports (without the country name)
                Kind = "country",
                Label = string.IsNullOrWhiteSpace(req.Country.Name) ? country?.Name ?? iso2 : req.Country.Name,
                CountryIso2 = country?.CountryIso2 ?? iso2,
                CountryIso3 = country?.CountryIso3?.ToUpperInvariant() ?? req.Country.Iso3?.ToUpperInvariant(),
                FocusLat = country?.Lat,
                FocusLng = country?.Lng
            });

        }

        // 3) Free text (kept your preview-based logic, unchanged except small refactors)
        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var trimmed = req.Q.Trim();
            var preview = await resolver.PreviewAsync(req.Q, ct);

            var keywords = (preview.NonGeoKeywords ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var keywordQuery = keywords.Length > 0
               ? string.Join(' ', keywords)
               : trimmed;

            string KwSuffix(string[] kw)
                => kw.Length == 0 ? string.Empty
                : "|kw:" + Uri.EscapeDataString(string.Join(' ', kw));


            // Prefer best city; else best country; else generic q:
            var bestCity = preview.CityMatches?.OrderByDescending(m => m.Score).FirstOrDefault();
            if (bestCity is not null)
            {
                var iso2 = (bestCity.CountryIso2 ?? "").ToUpperInvariant();
                var countryDto = string.IsNullOrWhiteSpace(iso2) ? null : await countryService.GetByIdAsync(iso2, ct);
                var cleanQ = SanitizeQueryForCountry(trimmed, iso2, countryDto?.Name, countryDto?.CountryIso3);
                var localName = bestCity.LocalName;
                if (string.IsNullOrWhiteSpace(localName))
                {
                    localName = await cityService.EnsureLocalNameAsync(bestCity, ct);
                }

                return Ok(new ResolveScopeResponse
                {
                    ScopeKey = CityScopeKey(bestCity.Name, iso2, localName, cleanQ) + KwSuffix(keywords),
                    Kind       = "city",
                    Label      = await BuildCityLabelWithCountryNameAsync(countryService, bestCity.Name, iso2, ct),
                    CountryIso2 = iso2,
                    CountryIso3= bestCity.CountryIso3?.ToUpperInvariant(),
                    CityId     = bestCity.Id,
                    FocusLat   = bestCity.Lat,
                    FocusLng   = bestCity.Lng,
                });
            }

            var bestCountry = preview.CountryMatches?.OrderByDescending(m => m.Score).FirstOrDefault();
            if (bestCountry is not null)

            {
                var iso2 = (bestCountry.CountryIso2 ?? bestCountry.Id ?? "").ToUpperInvariant();
                var countryDto = string.IsNullOrWhiteSpace(iso2) ? null : await countryService.GetByIdAsync(iso2, ct);
                var cleanQ = SanitizeQueryForCountry(trimmed, iso2, countryDto?.Name, countryDto?.CountryIso3);

                return Ok(new ResolveScopeResponse
                {
                    ScopeKey = CountryScopeKey(iso2, cleanQ) + KwSuffix(keywords),
                    Kind       = "country",
                    Label      = bestCountry.Name,
                    CountryIso2= iso2,
                    CountryIso3= bestCountry.CountryIso3?.ToUpperInvariant(),
                    FocusLat   = bestCountry.Lat,
                    FocusLng   = bestCountry.Lng,
                });
            }

            return Ok(new ResolveScopeResponse
            {
                ScopeKey = $"q:{trimmed}" + KwSuffix(keywords),
                Kind     = "query",
                Label    = trimmed,
            });
        }

        return BadRequest(new { error = "Provide either q, city, or country." });
    }
    private static async Task<string> BuildCityLabelWithCountryNameAsync(
    ICountryReadService countryService,
    string? cityName,
    string? iso2,
    CancellationToken ct)
    {
        var code = (iso2 ?? "").Trim().ToUpperInvariant();
        var dto = string.IsNullOrWhiteSpace(code) ? null : await countryService.GetByIdAsync(code, ct);
        var countryText = dto?.Name ?? code; // fallback to ISO2 if not found
                                             // Reuse your existing formatting (City, Suffix)
        return string.IsNullOrWhiteSpace(cityName)
            ? (countryText ?? string.Empty)
            : $"{cityName.Trim()}, {countryText}";
    }

    [HttpPost("reverse")]
    public async Task<ActionResult<ResolveScopeResponse>> Reverse(
        [FromBody] ReverseScopeRequest req,
        [FromServices] ICityReadService cityService,
        [FromServices] ICountryReadService countryService,
        CancellationToken ct)
    {
        //MODIFIED
        var flow = Request.Headers["X-SearchBar-Flow"].ToString();
        if (!string.IsNullOrWhiteSpace(flow))   
            Console.WriteLine($"SB flow (reverse): {flow}");
        //MODIFIED

        if (req.Lat is null || req.Lng is null ||
            double.IsNaN(req.Lat.Value) || double.IsInfinity(req.Lat.Value) ||
            double.IsNaN(req.Lng.Value) || double.IsInfinity(req.Lng.Value))
        {
            return BadRequest(new { error = "Provide numeric lat/lng." });
        }

        var lat = req.Lat.Value;
        var lng = req.Lng.Value;

        var city = await cityService.FindNearestAsync(lat, lng, CitySnapDistanceKm, ct);
        if (city is not null)
        {
            var iso2 = (city.CountryIso2 ?? "").ToUpperInvariant();
            var localName = city.LocalName;
            if (string.IsNullOrWhiteSpace(localName))
            {
                localName = await cityService.EnsureLocalNameAsync(city, ct);
            }
            var fallbackQ = city.Name;

            return Ok(new ResolveScopeResponse
            {
                ScopeKey = CityScopeKey(city.Name, iso2, localName, fallbackQ), // e.g., city:skopje-mk
                Kind       = "city",
                Label      = await BuildCityLabelWithCountryNameAsync(countryService, city.Name, iso2, ct),
                CountryIso2 = iso2,
                CountryIso3= city.CountryIso3?.ToUpperInvariant(),
                CityId     = city.Id,
                FocusLat   = city.Lat,
                FocusLng   = city.Lng
            });
        }

        var country = await countryService.FindNearestAsync(lat, lng, ct);
        if (country is not null)
        {
            var iso2 = (country.CountryIso2 ?? country.Id ?? "").ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(iso2))
            {
                return Ok(new ResolveScopeResponse
                {
                    ScopeKey   = CountryScopeKey(iso2, null),       // e.g., country:MK
                    Kind       = "country",
                    Label      = country.Name,
                    CountryIso2= iso2,
                    CountryIso3= country.CountryIso3?.ToUpperInvariant(),
                    FocusLat   = country.Lat,
                    FocusLng   = country.Lng
                });
            }
        }

        return NotFound(new { error = "No matching location." });
    }
}
