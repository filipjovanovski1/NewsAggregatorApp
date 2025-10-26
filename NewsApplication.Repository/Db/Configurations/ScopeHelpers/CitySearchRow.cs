using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Repository.Db.Configurations.ScopeHelpers
{
    [Keyless]
    internal sealed class CitySearchRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string CountryName { get; set; } = default!;
        public string CountryIso2 { get; set; } = default!;
        public string CountryIso3 { get; set; } = default!;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double Score { get; set; }
    }
}


