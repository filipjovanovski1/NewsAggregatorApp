using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.ImportedCSVs
{
    public sealed class CityCsvDTO
    {
        public string Name { get; set; } = null!;
        public string CountryCode { get; set; } = null!;  // ISO2
        public string CountryName { get; set; } = null!;
        public string Coordinates { get; set; } = null!;  // "lat, lon"

        public long? Population { get; set; }

    }
}
