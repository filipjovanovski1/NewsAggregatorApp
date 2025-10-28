using NewsApplication.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.Cache
{
    public sealed class ArticleCache : BaseEntity
    {
        public string ScopeKey { get; set; } = null!; // canonical(FinalScopeDTO, page)
        public int Page { get; set; }
        public string? NextPageToken { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public ICollection<ArticleCacheItem> Items { get; set; } = new List<ArticleCacheItem>();
    }
}
