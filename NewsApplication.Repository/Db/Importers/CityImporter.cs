using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DTOs.ImportedCSVs;
using NewsApplication.Domain.DTOs.Mappings;
using System.Globalization;
using System.Text.RegularExpressions;


namespace NewsApplication.Repository.Db.Importers;
public sealed class CityImporter
{
    private readonly ApplicationDbContext _db;
    public CityImporter(ApplicationDbContext db) => _db = db;

    public async Task<(int inserted, List<string> errors)> ImportAsync(
        string pathToCsv, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var inserted = 0;

        // Preload valid ISO2 codes
        var iso2Set = (await _db.Countries.Select(c => c.Iso2).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            IgnoreBlankLines = true,
            Mode = CsvMode.NoEscape,

            PrepareHeaderForMatch = a => (a.Header ?? string.Empty).Trim().TrimEnd(',')
        };

        using var reader = new StreamReader(pathToCsv);
        using var csv = new CsvReader(reader, cfg);
        
        csv.Context.RegisterClassMap<CityCsvMap>();

        var batch = new List<City>(1024);

        await foreach (var dto in csv.GetRecordsAsync<CityCsvDTO>(ct))
        {
            var row = csv.Context.Parser!.RawRow;

            // normalize
            var name = (dto.Name ?? "").Trim();
            var countryName = (dto.CountryName ?? "").Trim();
            var iso2 = (dto.CountryCode ?? "").Trim().ToUpperInvariant();
            var pop = dto.Population ?? 0;
            if (pop < 10_000)
            {
                // silently skip tiny settlements; or log if you prefer:
                // errors.Add($"Row {row}: population {pop} < 10k");
                continue;
            }
            // 1) Auto-generated alias map from your List_of_countries.csv (ISO != AFF_ISO)
            //    This translates unusual ISO codes into your canonical AFF_ISO codes.
            iso2 = iso2 switch
            {
                // Auto-generated from List_of_countries.csv where ISO != AFF_ISO
                // BEGIN AUTO-GENERATED
                "AC" => "GB",
                "AN" => "NL",
                "AQ" => "AQ",
                "AX" => "FI",
                "BL" => "FR",
                "BQ" => "NL",
                "BV" => "NO",
                "CC" => "AU",
                "CX" => "AU",
                "CW" => "NL",
                "DG" => "GB",
                "EA" => "ES",
                "EH" => "MA",
                "EU" => "EU",
                "FK" => "GB",
                "FO" => "DK",
                "GF" => "FR",
                "GG" => "GB",
                "GI" => "GB",
                "GL" => "DK",
                "GP" => "FR",
                "GS" => "GB",
                "GU" => "US",
                "HK" => "CN",
                "HM" => "AU",
                "IM" => "GB",
                "IO" => "GB",
                "JE" => "GB",
                "MF" => "FR",
                "MO" => "CN",
                "MP" => "US",
                "MQ" => "FR",
                "MS" => "GB",
                "NC" => "FR",
                "NF" => "AU",
                "PF" => "FR",
                "PM" => "FR",
                "PN" => "GB",
                "PR" => "US",
                "PS" => "PS",
                "RE" => "FR",
                "SJ" => "NO",
                "SX" => "NL",
                "TA" => "GB",
                "TF" => "FR",
                "UM" => "US",
                "VA" => "VA",
                "VG" => "GB",
                "VI" => "US",
                "WF" => "FR",
                "YT" => "FR",
                "XK" => "RS",
                "TW" => "CN",
                "AI" => "GB",
                "TC" => "GB",
                "KY" => "GB",
                "CK" => "NZ",
                _ => iso2
                // END AUTO-GENERATED
            };

            // 2) Safe extras for common territories → parent, but only if it helps:
            //    If the current iso2 is NOT in your Countries table, and the mapped target IS, then adopt the target.
            //    (Prevents false remaps when you already have separate rows for e.g. FO, GL, HK, etc.)
            var tryMap = iso2 switch
            {
                // Crown dependencies & BOTs
                "GG" => "GB",
                "JE" => "GB",
                "IM" => "GB",
                "GI" => "GB",
                "FK" => "GB",
                "VG" => "GB",
                "PN" => "GB",
                "MS" => "GB",
                "IO" => "GB",
                "GS" => "GB",
                "SH" => "GB",
                "TA" => "GB",
                "DG" => "GB",
                "AI" => "GB",
                "TC" => "GB",
                "KY" => "GB",
                "CK" => "NZ",
                // US territories
                "PR" => "US",
                "GU" => "US",
                "MP" => "US",
                "VI" => "US",
                "AS" => "US",
                "UM" => "US",
                // CN SARs
                "HK" => "CN",
                "MO" => "CN",
                // FR territories & collectivities
                "GF" => "FR",
                "PF" => "FR",
                "TF" => "FR",
                "NC" => "FR",
                "WF" => "FR",
                "PM" => "FR",
                "BL" => "FR",
                "MF" => "FR",
                "GP" => "FR",
                "RE" => "FR",
                "YT" => "FR",
                // AU external territories
                "CX" => "AU",
                "CC" => "AU",
                "NF" => "AU",
                "HM" => "AU",
                // DK realm
                "GL" => "DK",
                "FO" => "DK",
                // NL territories
                "CW" => "NL",
                "SX" => "NL",
                "BQ" => "NL",
                // NO territories
                "SJ" => "NO",
                // ES enclaves
                "EA" => "ES",
                // Misc where your table is likely canonical on parent
                "AC" => "GB", // Ascension under Saint Helena
                "VA" => "VA", // keep Vatican as itself
                "XK" => "RS",
                "TW" => "CN",
                _ => iso2
            };

            // Only adopt the extra mapping if it actually resolves into a known country in your DB.
            if (!iso2Set.Contains(iso2) && iso2Set.Contains(tryMap)) iso2 = tryMap;

            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"Row {row}: missing Name");
                continue;
            }
            if (iso2.Length != 2 || !iso2Set.Contains(iso2))
            {
                errors.Add($"Row {row}: invalid Country Code '{dto.CountryCode}'");
                continue;
            }

            // parse "lat, lon"
            if (!TryParseCoordinates(dto.Coordinates, out var lat, out var lng))
            {
                errors.Add($"Row {row}: invalid Coordinates '{dto.Coordinates}'");
                continue;
            }

            batch.Add(new City
            {
                Id = Guid.NewGuid(),
                Name = name,
                CountryName = countryName,
                CountryIso2 = iso2,
                Latitude = lat,
                Longitude = lng
            });

            if (batch.Count >= 1000) { inserted += await SaveBatch(batch, errors, row, ct); }
        }

        if (batch.Count > 0) { inserted += await SaveBatch(batch, errors, null, ct); }

        return (inserted, errors);
    }

    private async Task<int> SaveBatch(List<City> batch, List<string> errors, int? lastRow, CancellationToken ct)
    {
        _db.Cities.AddRange(batch);
        try
        {
            var count = batch.Count;
            await _db.SaveChangesAsync(ct);
            batch.Clear();
            return count;
        }
        catch (DbUpdateException ex)
        {
            errors.Add($"Batch ending at row {lastRow ?? -1}: {ex.GetBaseException().Message}");
            // detach so we can continue if desired
            foreach (var e in _db.ChangeTracker.Entries<City>()) e.State = EntityState.Detached;
            batch.Clear();
            return 0;
        }
    }

    private static bool TryParseCoordinates(string? s, out double lat, out double lng)
    {
        lat = lng = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Remove quotes, trim, then extract the first two numeric tokens
        var cleaned = s.Replace("\"", "").Trim();

        // Matches numbers like -12, 34.56, +78.9 (no thousands separators)
        var matches = Regex.Matches(cleaned, @"[-+]?\d+(?:\.\d+)?");
        if (matches.Count < 2) return false;

        if (!double.TryParse(matches[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) return false;
        if (!double.TryParse(matches[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return false;

        // Heuristic: if the first value is outside latitude range, assume order is lon,lat and swap
        var maybeLat = Math.Abs(a) <= 90 ? a : b;
        var maybeLng = Math.Abs(a) <= 90 ? b : a;

        if (maybeLat < -90 || maybeLat > 90 || maybeLng < -180 || maybeLng > 180) return false;

        lat = maybeLat;
        lng = maybeLng;
        return true;
    }

}
