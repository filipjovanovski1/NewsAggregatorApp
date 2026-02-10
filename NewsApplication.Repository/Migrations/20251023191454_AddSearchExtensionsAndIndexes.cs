using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchExtensionsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') THEN
                    CREATE EXTENSION unaccent;
                END IF;
            END$$;
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                    CREATE EXTENSION pg_trgm;
                END IF;
            END$$;
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION immutable_unaccent(text)
            RETURNS text
            LANGUAGE sql
            IMMUTABLE
            AS $$
              SELECT public.unaccent($1);
            $$;
            """, suppressTransaction: true);

            migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_cities_name_trgm
            ON "Cities" USING gin ((lower(immutable_unaccent("Name"))) gin_trgm_ops);
            """, suppressTransaction: true);

                    migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_countries_name_trgm
            ON "Countries" USING gin ((lower(immutable_unaccent("Name"))) gin_trgm_ops);
            """, suppressTransaction: true);

            migrationBuilder.Sql(@"
            CREATE INDEX IF NOT EXISTS idx_countries_iso2
            ON ""Countries"" (""Iso2"");
            ", suppressTransaction: true);
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
