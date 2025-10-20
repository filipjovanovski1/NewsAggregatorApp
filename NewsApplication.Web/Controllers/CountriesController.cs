#if false
using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Dtos;
using NewsApplication.Service.Interfaces;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/countries")]
public sealed class CountriesController : ControllerBase
{
    private readonly IArticleReadService _articles;
    public CountriesController(IArticleReadService articles) => _articles = articles;

    // GET /api/countries/MK/articles?page=1&pageSize=20
    [HttpGet("{iso2:alpha:length(2)}/articles")]
    public Task<PagedResult<ArticleDto>> ByCountry([FromRoute] string iso2,
                                                   [FromQuery] int page = 1,
                                                   [FromQuery] int pageSize = 20,
                                                   CancellationToken ct = default)
        => _articles.GetCountryArticlesAsync(iso2.ToUpperInvariant(), page, pageSize, ct);

    // GET /api/countries/MK/cities/{cityId}/articles?page=1&pageSize=20
    [HttpGet("{iso2:alpha:length(2)}/cities/{cityId}/articles")]
    public Task<PagedResult<ArticleDto>> ByCity([FromRoute] string iso2,
                                                [FromRoute] string cityId,
                                                [FromQuery] int page = 1,
                                                [FromQuery] int pageSize = 20,
                                                CancellationToken ct = default)
        => _articles.GetCityArticlesAsync(iso2.ToUpperInvariant(), cityId, page, pageSize, ct);
}
#endif