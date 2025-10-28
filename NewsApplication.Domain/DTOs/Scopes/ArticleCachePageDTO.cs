using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes;
    public sealed record ArticleCachePageDTO(
    Guid Id, string ScopeKey, int Page, string? NextPageToken, DateTimeOffset ExpiresAt,
    IReadOnlyList<ArticleCacheItemDTO> Items);
