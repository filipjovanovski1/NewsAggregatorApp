using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DTOs.ImportedCSVs;
using NewsApplication.Domain.DTOs.Mappings;
using NewsApplication.Repository.Db;
using System.Globalization;


namespace NewsApplication.Repository.Db.Importers;
public sealed class CountryImporter
{
    private readonly ApplicationDbContext _db;
    public CountryImporter(ApplicationDbContext db) => _db = db;

    public async Task<(int upserted, List<string> errors)> ImportAsync(
        string csvPath, CancellationToken ct = default)
    {
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            PrepareHeaderForMatch = a => a.Header.Trim()
        };

        var errors = new List<string>();
        var upserted = 0;

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, cfg);
        csv.Context.RegisterClassMap<CountryCsvMap>();

        await foreach (var r in csv.GetRecordsAsync<CountryCsvDTO>(ct))
        {
            var row = csv.Context.Parser!.RawRow;

            // filter: only keep rows where COUNTRY == COUNTRYAFF
            var country = (r.COUNTRY ?? "").Trim();
            var countryAff = (r.COUNTRYAFF ?? "").Trim();
            if (!string.Equals(country, countryAff, StringComparison.Ordinal))
                continue;

            var iso2 = (r.AFF_ISO ?? "").Trim().ToUpperInvariant();
            if (iso2.Length != 2)
            {
                errors.Add($"Row {row}: invalid AFF_ISO '{r.AFF_ISO}'");
                continue;
            }

            var name = countryAff; // we store COUNTRYAFF as Name

            if (!TryParseDouble(r.latitude, out var lat) ||
                !TryParseDouble(r.longitude, out var lng))
            {
                // allow missing coords: store as nulls but log
                errors.Add($"Row {row}: bad coords lat='{r.latitude}' lon='{r.longitude}' (storing nulls)");
                lat = null; lng = null;
            }

            // Upsert by PK = Iso2
            var existing = await _db.Countries.FindAsync([iso2], ct);
            if (existing is null)
            {
                _db.Countries.Add(new Country
                {
                    Iso2 = iso2,
                    Name = name,
                    CentroidLat = lat,
                    CentroidLng = lng
                });
            }
            else
            {
                // Update name/coords if changed
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                    existing.Name = name;
                if (existing.CentroidLat != lat) existing.CentroidLat = lat;
                if (existing.CentroidLng != lng) existing.CentroidLng = lng;
            }

            upserted++;
        }

        await _db.SaveChangesAsync(ct);
        return (upserted, errors);
    }

    private static bool TryParseDouble(string? s, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = d;
            return true;
        }
        return false;
    }
}
