using CsvHelper.Configuration;
using NewsApplication.Domain.DTOs.ImportedCSVs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Domain.DTOs.Mappings
{
    public sealed class CityCsvMap : ClassMap<CityCsvDTO>
    {
        public CityCsvMap()
        {
            // Map headers exactly; use .Name(...) for spaces/mixed case
            Map(m => m.Name).Name("Name");
            Map(m => m.CountryCode).Name("Country Code");
            Map(m => m.CountryName).Name("Country name EN");
            Map(m => m.Coordinates).Name("Coordinates");
            Map(m => m.Population).Convert(row =>
            {
                var s = row.Row.GetField("Population")?.Trim();
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    return v;
                return null; // junk like "Europe/Moscow" becomes null
            });
        }
    }
}
