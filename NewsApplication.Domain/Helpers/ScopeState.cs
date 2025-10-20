using NewsApplication.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.Helpers
{
    public sealed class ScopeState
    {
        public Guid Id { get; set; }                 // gen_random_uuid()
        public ScopeKind Kind { get; set; }          // City or Country (never Other)
        public Guid? CityId { get; set; }
        public string? CountryIso2 { get; set; }

        public DateTime LastAccessTime { get; set; }  // updated on every click/serve
        public DateTime ExpirationTime { get; set; }   // LastAccess + 1 hour
    }
}
