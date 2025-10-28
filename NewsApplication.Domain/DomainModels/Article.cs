using NewsApplication.Domain.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DomainModels
{
    public class Article 
    {
        [Key]
        
        public required string ArticleId { get; set; }
        public string Provider { get; set; } = null!;   // no default here HAVE TO SET TO "NEWSDATA" LATER
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string Publisher { get; set; } = null!;
        public string Url { get; set; } = null!;               // canonical URL to open
        public DateTime PublishedTime { get; set; }
        public List<string> Categories { get; set; } = new();
        public DateTimeOffset InsertedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
