using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db.Configurations.ScopeHelpers;
using NewsApplication.Repository.Db.Interfaces;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Repository.Db.Implementations
{
    public sealed class CityReadRepository : ICityReadRepository
    {
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        public CityReadRepository(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

        private const string Sql = @"
        WITH q(term) AS (VALUES (lower(unaccent(@token))))
        SELECT c.""Id"", c.""Name"", c.""CountryName"", c.""CountryIso2"",
               co.""Iso3"" AS ""CountryIso3"", 
               c.""Latitude"", c.""Longitude"", c.""LocalName"",
               similarity(lower(unaccent(c.""Name"")), q.term) AS score
        FROM ""Cities"" c
        JOIN ""Countries"" co ON co.""Iso2"" = c.""CountryIso2""   -- ← join to get Iso3
        , q
        WHERE lower(unaccent(c.""Name"")) LIKE '%'||q.term||'%'
        ORDER BY CASE
                   WHEN lower(unaccent(c.""Name"")) = q.term THEN 0
                   WHEN lower(unaccent(c.""Name"")) LIKE q.term||'%' THEN 1
                   ELSE 2
                 END,
                 score DESC, c.""Id""
        LIMIT @limit;";

        public async Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string normalizedToken, int limit, CancellationToken ct)
        {
            var token = new NpgsqlParameter("token", normalizedToken);
            var lim = new NpgsqlParameter("limit", limit);

            await using var db = await _factory.CreateDbContextAsync(ct);

            var rows = await db.Set<CitySearchRow>()
                .FromSqlRaw(Sql, token, lim)
                .AsNoTracking()
                .ToListAsync(ct);

            return rows.Select(r => r.ToDTO()).ToList();
        }

        public async Task<GeoCandidateDTO?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            // If you have a City entity mapped in your DbContext, this is cheap and simple:
            await using var db = await _factory.CreateDbContextAsync(ct);

            var dto = await db.Cities
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.CountryName,
                    c.CountryIso2,
                    Iso3 = c.Country != null ? c.Country.Iso3 : null,
                    c.Latitude,
                    c.Longitude,
                    c.LocalName
                })
                .FirstOrDefaultAsync(ct);

            if (dto is null) return null;

            return new GeoCandidateDTO
            {
                Id = dto.Id.ToString(),
                Name = dto.Name,
                CountryName = dto.CountryName,
                CountryIso2 = dto.CountryIso2?.ToUpperInvariant(),
                CountryIso3 = dto.Iso3?.ToUpperInvariant(),
                Lat = dto.Latitude,
                Lng = dto.Longitude,
                LocalName = dto.LocalName,
                Score = 1.0
            };
        }

        public async Task<IReadOnlyList<GeoCandidateDTO>> FindNearestAsync(double lat, double lng, int limit, CancellationToken ct)
        {
            var take = Math.Clamp(limit, 1, 50);

            await using var db = await _factory.CreateDbContextAsync(ct);

            var rows = await db.Cities
                .AsNoTracking()
                .OrderBy(c =>
                    ((c.Latitude - lat) * (c.Latitude - lat)) +
                    ((c.Longitude - lng) * (c.Longitude - lng)))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.CountryName,
                    c.CountryIso2,
                    Iso3 = c.Country != null ? c.Country.Iso3 : null,
                    c.Latitude,
                    c.Longitude,
                    c.LocalName
                })
                .Take(take)
                .ToListAsync(ct);

            return rows
                .Select(r => new GeoCandidateDTO
                {
                    Id = r.Id.ToString(),
                    Name = r.Name,
                    CountryName = r.CountryName,
                    CountryIso2 = r.CountryIso2?.ToUpperInvariant(),
                    CountryIso3 = r.Iso3?.ToUpperInvariant(),
                    Lat = r.Latitude,
                    Lng = r.Longitude,
                    LocalName = r.LocalName,
                    Score = 1.0
                })
                .ToList();
        }
        public async Task<string?> SetLocalNameAsync(Guid cityId, string localName, CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var city = await db.Cities
                .FirstOrDefaultAsync(c => c.Id == cityId, ct);

            if (city is null) return null;
            if (!string.IsNullOrWhiteSpace(city.LocalName)) return city.LocalName;

            var trimmed = localName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return city.LocalName;

            city.LocalName = trimmed;
            await db.SaveChangesAsync(ct);
            return city.LocalName;
        }

       
    }
}
