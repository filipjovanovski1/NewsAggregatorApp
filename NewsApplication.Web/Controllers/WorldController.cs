#if false
using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Dtos;
using NewsApplication.Service.Interfaces;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/world")]
public sealed class WorldController : ControllerBase
{
    private readonly IArticleReadService _articles;
    public WorldController(IArticleReadService articles) => _articles = articles;

    // GET /api/world/articles?page=1&pageSize=20
    [HttpGet("articles")]
    public Task<PagedResult<ArticleDto>> Get([FromQuery] int page = 1,
                                             [FromQuery] int pageSize = 5,
                                             CancellationToken ct = default)
        => _articles.GetTopWorldArticlesAsync(page, pageSize, ct);
}
#endif