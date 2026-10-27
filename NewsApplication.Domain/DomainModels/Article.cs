using NewsApplication.Domain.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DomainModels
{
    public class Article : BaseEntity
    {
        public required string ProviderArticleId { get; set; }
        public string Provider { get; set; } = null!;   // no default here HAVE TO SET TO "NEWSDATA" LATER
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string Publisher { get; set; } = null!;
        public string Url { get; set; } = null!;               // canonical URL to open
        public DateTime PublishedTime { get; set; }
        public DateTime? ModifiedTime { get; set; }
        public List<string> Categories { get; set; } = new();
        public string? CanonicalUrl { get; set; }
        public string? CanonicalUrlHash { get; set; }
        public string? ContentText { get; set; }
        public string? ContentHash { get; set; }
        public string? Author { get; set; }
        public string? Language { get; set; }
        public DateTimeOffset? ExtractedAt { get; set; }
        public string? ExtractionStatus { get; set; }
        public string? Extractor { get; set; }
        public string? ExtractorVersion { get; set; }
        public DateTimeOffset InsertedAt { get; set; } = DateTimeOffset.UtcNow;

        public ICollection<ArticleFeedEntry> FeedEntries { get; set; } = new List<ArticleFeedEntry>();
    }
}
