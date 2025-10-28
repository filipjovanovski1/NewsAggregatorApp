using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes;

public sealed record ArticleCacheItemDTO(
string ArticleId, int? Position, ArticleDTO Article);
