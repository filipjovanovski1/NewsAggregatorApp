using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Scopes
{
    public sealed class TopCityDTO
    {
        public string Id { get; init; } = default!;
        public string Name { get; init; } = default!;
        public string CountryName { get; init; } = default!;
        public string CountryIso2 { get; init; } = default!;
        public string? CountryIso3 { get; init; }
        public double? Lat { get; init; }
        public double? Lng { get; init; }
        public long Population { get; init; }
    }
}
