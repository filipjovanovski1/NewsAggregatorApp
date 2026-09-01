using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes;

public sealed record ArticleCacheItemDTO(
Guid ArticleId, int? Position, ArticleDTO Article);
