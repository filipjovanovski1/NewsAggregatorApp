using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class CreateSearchIndexesConcurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION immutable_unaccent(text)
                RETURNS text AS $$
                SELECT unaccent('public.unaccent', $1)
                $$ LANGUAGE sql IMMUTABLE;
            ", suppressTransaction: true);

            migrationBuilder.Sql(@"
                DROP INDEX CONCURRENTLY IF EXISTS idx_cities_name_trgm;
                CREATE INDEX CONCURRENTLY idx_cities_name_trgm
                ON ""Cities"" USING gin ((lower(immutable_unaccent(""Name""))) gin_trgm_ops);
            ", suppressTransaction: true);

            migrationBuilder.Sql(@"
                DROP INDEX CONCURRENTLY IF EXISTS idx_countries_name_trgm;
                CREATE INDEX CONCURRENTLY idx_countries_name_trgm
                ON ""Countries"" USING gin ((lower(immutable_unaccent(""Name""))) gin_trgm_ops);
            ", suppressTransaction: true);

            migrationBuilder.Sql(@"
                DROP INDEX CONCURRENTLY IF EXISTS idx_countries_iso2;
                CREATE INDEX CONCURRENTLY idx_countries_iso2
                ON ""Countries"" (""Iso2"");
            ", suppressTransaction: true);

                    }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS idx_cities_name_trgm;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS idx_countries_name_trgm;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS idx_countries_iso2;", suppressTransaction: true);
        }
    }
}
