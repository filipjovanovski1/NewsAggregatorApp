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
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string Publisher { get; set; } = null!;
        public string Category { get; set; } = null!;           // or make an enum later
        public string Link { get; set; } = null!;               // canonical URL to open
        public DateTime PublishedTime { get; set; }
        public ICollection<ArticleScope> Scopes { get; set; } = new List<ArticleScope>();
    }
}
