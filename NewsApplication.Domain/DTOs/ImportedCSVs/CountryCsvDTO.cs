using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace NewsApplication.Domain.DTOs.ImportedCSVs
{
    public sealed class CountryCsvDTO
    {
        public string COUNTRY { get; set; } = null!;
        public string ISO { get; set; } = null!;
        public string? longitude { get; set; }     // keep as string, we'll parse safely
        public string? latitude { get; set; }
        public string? ISO3 { get; set; }
    }
}
