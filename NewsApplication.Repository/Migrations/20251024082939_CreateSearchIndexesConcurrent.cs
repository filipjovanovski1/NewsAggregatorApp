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
            // Ensure the immutable wrapper exists
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION immutable_unaccent(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                AS $$
                  SELECT unaccent($1);
                $$;
            ");

            // Cities name trigram index (accent- & case-insensitive)
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_cities_name_trgm;
                CREATE INDEX IF NOT EXISTS idx_cities_name_trgm
                ON ""Cities"" USING gin ((lower(immutable_unaccent(""Name""))) gin_trgm_ops);
            ");

            // Countries name trigram index (accent- & case-insensitive)
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_countries_name_trgm;
                CREATE INDEX IF NOT EXISTS idx_countries_name_trgm
                ON ""Countries"" USING gin ((lower(immutable_unaccent(""Name""))) gin_trgm_ops);
            ");

            // Countries ISO2 btree index
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS idx_countries_iso2;
                CREATE INDEX IF NOT EXISTS idx_countries_iso2
                ON ""Countries"" (""Iso2"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_cities_name_trgm;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_countries_name_trgm;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS idx_countries_iso2;");
        }
    }
}
