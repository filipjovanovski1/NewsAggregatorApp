#if false
using Microsoft.AspNetCore.Mvc;
using NewsApplication.Web.Models;           // SearchResultDto, ReverseResultDto
using NewsApplication.Service.Interfaces;   // IGeoQueryService

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly IGeoQueryService _geo;
    public SearchController(IGeoQueryService geo) => _geo = geo;

    // GET /api/search?q=skop&take=12
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchResultDto>>> Search([FromQuery] string q,
                                                                           [FromQuery] int take = 12,
                                                                           CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(q)
            ? Ok(Array.Empty<SearchResultDto>())
            : Ok(await _geo.SearchAsync(q, take, ct));

    // GET /api/search/reverse?lat=...&lng=...
    [HttpGet("reverse")]
    public async Task<ActionResult<ReverseResultDto?>> Reverse([FromQuery] double lat,
                                                               [FromQuery] double lng,
                                                               CancellationToken ct = default)
        => Ok(await _geo.ReverseAsync(lat, lng, ct));
}
#endif