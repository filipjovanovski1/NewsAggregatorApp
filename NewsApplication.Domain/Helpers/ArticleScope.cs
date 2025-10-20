using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.Enums;

namespace NewsApplication.Domain.Helpers
{
    public class ArticleScope : BaseEntity   
    {
        public required string ArticleId { get; set; }
        public Article Article { get; set; } = null!;

        public ScopeKind Kind { get; set; }             // City | Country | Other

        // Exactly ONE of these is set depending on Kind:
        public Guid? CityId { get; set; }               // when Kind == City
        public string? CountryIso2 { get; set; }        // when Kind == Country
        public string? OtherValue { get; set; }         // when Kind == Other
    }
}
