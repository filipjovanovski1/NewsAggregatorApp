using NewsApplication.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NewsApplication.Domain.Cache
{
    public class ArticleCacheItem
    {
        public Guid ArticleCacheId { get; set; }     // FK → ArticleCache.Id (Guid)
        [JsonIgnore]
        public ArticleCache ArticleCache { get; set; } = null!;

        public string ArticleId { get; set; } = null!;  // FK → Article.ArticleId
        public Article Article { get; set; } = null!;

        public int? Position { get; set; } // optional UI order
    }
}
