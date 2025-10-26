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

            var country = (r.COUNTRY ?? "").Trim();

            // ISO2 (required)
            var iso2 = (r.ISO ?? "").Trim().ToUpperInvariant();
            if (iso2.Length != 2)
            {
                errors.Add($"Row {row}: invalid ISO2 '{r.ISO}'");
                continue;
            }

            // ISO3 (optional but preferred)
            string? iso3 = (r.ISO3 ?? "").Trim();
            if (iso3.Length == 0)
            {
                iso3 = null; // allow null if the source row lacks ISO3
            }
            else
            {
                iso3 = iso3.ToUpperInvariant();
                if (iso3.Length != 3)
                {
                    errors.Add($"Row {row}: invalid ISO3 '{r.ISO3}' (storing NULL)");
                    iso3 = null;
                }
            }

            // Name from COUNTRY
            var name = country;

            // Parse coords (same behavior)
            if (!TryParseDouble(r.latitude, out var lat) ||
                !TryParseDouble(r.longitude, out var lng))
            {
                errors.Add($"Row {row}: bad coords lat='{r.latitude}' lon='{r.longitude}' (storing nulls)");
                lat = null; lng = null;
            }

            // Upsert by PK = Iso2 (Iso2 remains canonical)
            var existing = await _db.Countries.FindAsync([iso2], ct);
            if (existing is null)
            {
                _db.Countries.Add(new Country
                {
                    Iso2 = iso2,
                    Iso3 = iso3,             // <-- new
                    Name = name,
                    CentroidLat = lat,
                    CentroidLng = lng
                });
            }
            else
            {
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                    existing.Name = name;
                if (existing.CentroidLat != lat) existing.CentroidLat = lat;
                if (existing.CentroidLng != lng) existing.CentroidLng = lng;

                // Only set Iso3 when it's provided (don’t overwrite with null by accident)
                if (!string.IsNullOrWhiteSpace(iso3) && !string.Equals(existing.Iso3, iso3, StringComparison.Ordinal))
                    existing.Iso3 = iso3;
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
