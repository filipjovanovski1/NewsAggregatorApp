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
               c.""Latitude"", c.""Longitude"",
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
                .Select(c => new GeoCandidateDTO
                {
                    Id = c.Id.ToString(),
                    Name = c.Name,
                    CountryName = c.CountryName,
                    CountryIso2 = c.CountryIso2,
                    Lat = c.Latitude,
                    Lng = c.Longitude,
                    Score = 1.0
                })
                .FirstOrDefaultAsync(ct);

            return dto;
        }
    }
}
