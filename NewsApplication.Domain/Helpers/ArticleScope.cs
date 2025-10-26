using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.Enums;

namespace NewsApplication.Domain.Helpers
{
    // Article Scope is a class which contains a link to an article (one unique article), the kind of scope,
    // whether it is a unique query like "paris france ai" or a city/country (geo) query like "france" or
    // "paris", and stores it as a cache value for when a user searches a specific query/clicks on a specific
    // country/city (geo) in a short amount of time. The point of this class is to cache information so API
    // tokens aren't wasted by multiple users or the same user concurrently when re-using same query params
    // and "paths" to articles are de-duped.
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
