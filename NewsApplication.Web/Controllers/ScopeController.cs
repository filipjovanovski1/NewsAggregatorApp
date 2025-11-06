using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Interfaces;

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
        public int Id { get; set; }                    // your City PK
        public string Name { get; set; } = default!;
        public string CountryIso2 { get; set; } = default!;
    }

    public sealed class CountryPick
    {
        public string Iso2 { get; set; } = default!;   // e.g., "MK"
        public string Name { get; set; } = default!;
    }

    public sealed class ResolveScopeResponse
    {
        public string ScopeKey { get; set; } = default!;
        public string Kind { get; set; } = default!;   // "city" | "country" | "query"
        public string Label { get; set; } = default!;  // user-facing label to show in the searchbar
    }

    private static string MapCityToScopeKey(CityPick c)
        => $"city:{c.Id}";                   // <-- swap to your canonical format if different

    private static string MapCountryToScopeKey(CountryPick c)
        => $"country:{c.Iso2.ToLower()}";    // <-- swap to your canonical format if different

    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveScopeResponse>> Resolve(
        [FromBody] ResolveScopeRequest req,
        [FromServices] IScopeResolverService resolver,
        CancellationToken ct)
    {
        // 1) Explicit picks (preferred and unambiguous)
        if (req.City is not null)
        {
            var key = MapCityToScopeKey(req.City);
            return Ok(new ResolveScopeResponse { ScopeKey = key, Kind = "city", Label = req.City.Name });
        }
        if (req.Country is not null)
        {
            var key = MapCountryToScopeKey(req.Country);
            return Ok(new ResolveScopeResponse { ScopeKey = key, Kind = "country", Label = req.Country.Name });
        }

        // 2) Free text → use your resolver to find the best target, then map to scopeKey
        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var preview = await resolver.PreviewAsync(req.Q, ct);

            // Prefer best city match; else best country; else fallback to a generic "query:" scope
            var bestCity = preview.CityMatches?.OrderByDescending(m => m.Score).FirstOrDefault();
            if (bestCity is not null)
                return Ok(new ResolveScopeResponse
                {
                    ScopeKey = $"city:{bestCity.CityId}",            // <-- align with your real scheme
                    Kind = "city",
                    Label = bestCity.Display
                });

            var bestCountry = preview.CountryMatches?.OrderByDescending(m => m.Score).FirstOrDefault();
            if (bestCountry is not null)
                return Ok(new ResolveScopeResponse
                {
                    ScopeKey = $"country:{bestCountry.Iso2.ToLower()}",
                    Kind = "country",
                    Label = bestCountry.Display
                });

            // Non-geo search scope (your system supports non-geo queries too)
            return Ok(new ResolveScopeResponse
            {
                ScopeKey = $"query:{req.Q.Trim()}",
                Kind = "query",
                Label = req.Q.Trim()
            });
        }

        return BadRequest(new { error = "Provide either q, city, or country." });
    }
}
