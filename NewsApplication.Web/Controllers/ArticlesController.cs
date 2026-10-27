using Microsoft.AspNetCore.Mvc;
using NewsApplication.Service.Interfaces;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("articles")]
public sealed class ArticlesController : ControllerBase
{
    private const int UiPageSize = 6;

    /// <summary>
    /// Reads articles extracted by the RSS ingestion service. Geographic filtering is based
    /// on the source scopes produced by news-source-discovery; no provider API is called.
    /// </summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string scopeKey,
        [FromQuery] int uiPage,
        [FromServices] IStoredArticleSearchService search,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
            return BadRequest(new { error = "scopeKey is required" });

        uiPage = Math.Max(1, uiPage);
        var offset = (uiPage - 1) * UiPageSize;
        var result = await search.SearchAsync(scopeKey, offset, UiPageSize, ct);
        var hasOlder = result.Total > offset + result.Items.Count;

        return Ok(new
        {
            scopeKey,
            uiPage,
            pageSize = UiPageSize,
            hasNewer = uiPage > 1,
            hasOlder,
            totalDistinct = result.Total,
            nextUiPage = hasOlder ? uiPage + 1 : uiPage,
            items = result.Items.Select(article => new
            {
                articleId = article.Id,
                provider = article.Provider,
                title = article.Title,
                description = article.Description,
                imageUrl = article.ImageUrl,
                publisher = article.Publisher,
                url = article.Url,
                publishedTime = article.PublishedTime,
                categories = article.Categories
            })
        });
    }

    /// <summary>
    /// Retained temporarily for older web clients. RSS articles are already persisted, so
    /// there is no upstream provider page to prefetch.
    /// </summary>
    [HttpPost("cache/fetch")]
    public IActionResult FetchCachePage([FromQuery] string scopeKey, [FromQuery] int page) =>
        Ok(new { scopeKey, page, prefetched = false, source = "database" });
}
