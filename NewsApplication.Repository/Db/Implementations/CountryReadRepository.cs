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
    public sealed class CountryReadRepository : ICountryReadRepository
    {
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        public CountryReadRepository(IDbContextFactory<ApplicationDbContext> factory) => _factory = factory;

        private const string Sql = @"
        WITH q(term) AS (VALUES (lower(unaccent(@token))))
        SELECT
            co.""Iso2"" AS ""CountryIso2"",
            co.""Iso3"" AS ""CountryIso3"",
            co.""Name"",
            co.""CentroidLat"" AS ""Latitude"",
            co.""CentroidLng"" AS ""Longitude"",
            similarity(lower(unaccent(co.""Name"")), q.term) AS score
        FROM ""Countries"" co, q
        WHERE lower(unaccent(co.""Name"")) LIKE '%'||q.term||'%'
           OR lower(co.""Iso2"") = q.term
           OR lower(co.""Iso3"") = q.term
        ORDER BY
            CASE
                 WHEN lower(unaccent(co.""Name"")) = q.term
                 OR lower(co.""Iso2"") = q.term
                 OR lower(co.""Iso3"") = q.term           -- <— add this
                    THEN 0
                 WHEN lower(unaccent(co.""Name"")) LIKE q.term||'%' THEN 1
                    ELSE 2
            END,
            score DESC,
            co.""Iso2""            -- <-- tie-breaker by Iso2
        LIMIT @limit;";


        public async Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string normalizedToken, int limit, CancellationToken ct)
        {
            var token = new NpgsqlParameter("token", normalizedToken);
            var lim = new NpgsqlParameter("limit", limit);

            await using var db = await _factory.CreateDbContextAsync(ct);

            var rows = await db.Set<CountrySearchRow>()
                .FromSqlRaw(Sql, token, lim)
                .AsNoTracking()
                .ToListAsync(ct);

            return rows.Select(r => r.ToDTO()).ToList();
        }

        public async Task<GeoCandidateDTO?> GetByIdAsync(string iso2, CancellationToken ct)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var dto = await db.Countries
                .AsNoTracking()
                 .Where(c => c.Iso2 == iso2)
                .Select(c => new
                {
                    c.Iso2,
                    c.Iso3,
                    c.Name,
                    c.CentroidLat,
                    c.CentroidLng
                })
                .FirstOrDefaultAsync(ct);
            if (dto is null) return null;

            return new GeoCandidateDTO
            {
                Id = dto.Iso2?.ToUpperInvariant() ?? "??",
                Name = dto.Name,
                CountryName = null,
                CountryIso2 = dto.Iso2?.ToUpperInvariant(),
                CountryIso3 = dto.Iso3?.ToUpperInvariant(),
                Lat = dto.CentroidLat,
                Lng = dto.CentroidLng,
                Score = 1.0
            };
        }
        public async Task<IReadOnlyList<GeoCandidateDTO>> FindNearestAsync(double lat, double lng, int limit, CancellationToken ct)
        {
            var take = Math.Clamp(limit, 1, 50);

            await using var db = await _factory.CreateDbContextAsync(ct);

            var rows = await db.Countries
                .AsNoTracking()
                .Where(c => c.CentroidLat != null && c.CentroidLng != null)
                .OrderBy(c =>
                    ((c.CentroidLat!.Value - lat) * (c.CentroidLat!.Value - lat)) +
                    ((c.CentroidLng!.Value - lng) * (c.CentroidLng!.Value - lng)))
                .Select(c => new
                {
                    c.Iso2,
                    c.Iso3,
                    c.Name,
                    c.CentroidLat,
                    c.CentroidLng
                })
                .Take(take)
                .ToListAsync(ct);

            return rows
                .Select(r => new GeoCandidateDTO
                {
                    Id = r.Iso2?.ToUpperInvariant() ?? "??",
                    Name = r.Name,
                    CountryName = null,
                    CountryIso2 = r.Iso2?.ToUpperInvariant(),
                    CountryIso3 = r.Iso3?.ToUpperInvariant(),
                    Lat = r.CentroidLat,
                    Lng = r.CentroidLng,
                    Score = 1.0
                }).ToList();
        }
    }
}
