using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes;
    public sealed record ArticleDTO(
    string ArticleId, string Provider, string Title, string? Description,
    string? ImageUrl, string Publisher, string Url, DateTime PublishedTime,
    List<string> Categories);

