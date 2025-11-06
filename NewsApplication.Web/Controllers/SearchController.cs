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
        => Ok(await svc.PreviewAsync(q, ct)); // best-fit with bigrams + ISO promotion + thresholds :contentReference[oaicite:14]{index=14}
}
