using CsvHelper.Configuration;
using NewsApplication.Domain.DTOs.ImportedCSVs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Mappings
{
    public sealed class CountryCsvMap : ClassMap<CountryCsvDTO>
    {
        public CountryCsvMap()
        {
            Map(m => m.COUNTRY).Name("COUNTRY");
            Map(m => m.COUNTRYAFF).Name("COUNTRYAFF");
            Map(m => m.AFF_ISO).Name("AFF_ISO");
            Map(m => m.longitude).Name("longitude");
            Map(m => m.latitude).Name("latitude");
        }
    }

}
