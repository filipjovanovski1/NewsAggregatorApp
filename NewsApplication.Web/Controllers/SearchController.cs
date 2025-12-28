using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Interfaces;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("search")]
public sealed class SearchController : ControllerBase
{
    [HttpGet("city")]
    public async Task<IActionResult> SearchCity(
        [FromQuery] string q,
        [FromServices] ICityReadService svc,
        CancellationToken ct)
        => Ok(await svc.SearchAsync(q, 10, ct)); // normalized search via tokenizer in service :contentReference[oaicite:12]{index=12}

    [HttpGet("country")]
    public async Task<IActionResult> SearchCountry(
        [FromQuery] string q,
        [FromServices] ICountryReadService svc,
        CancellationToken ct)
        => Ok(await svc.SearchAsync(q, 10, ct)); // ditto for countries :contentReference[oaicite:13]{index=13}

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] string q,
        [FromServices] IScopeResolverService svc,
        CancellationToken ct)

    {
        var flow = Request.Headers["X-SearchBar-Flow"].ToString();
        if (!string.IsNullOrWhiteSpace(flow))
            Console.WriteLine($"SB flow (preview): {flow}");

        var result = await svc.PreviewAsync(q, ct);
        return Ok(result);
    }
    [HttpGet("cities/top")]
    public async Task<IActionResult> GetTopCities(
       [FromQuery] string countryIso2,
       [FromServices] ICityReadService svc,
       CancellationToken ct,
       [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(countryIso2)) return BadRequest("countryIso2 is required");
        var safeLimit = limit <= 0 ? 20 : Math.Min(limit, 100);
        var data = await svc.GetTopByPopulationAsync(countryIso2, safeLimit, ct);
        return Ok(data);
    }
}
