using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NewsApplication.Domain.DTOs.Discovery;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS unaccent;
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE OR REPLACE FUNCTION immutable_unaccent(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                AS $$
                  SELECT public.unaccent($1);
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "ArticleCaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "text", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    NextPageToken = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderArticleId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    PublishedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Categories = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    InsertedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CitySearchRow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CountryName = table.Column<string>(type: "text", nullable: false),
                    CountryIso2 = table.Column<string>(type: "text", nullable: false),
                    CountryIso3 = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Population = table.Column<long>(type: "bigint", nullable: true),
                    LocalName = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Iso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Iso3 = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CentroidLat = table.Column<double>(type: "double precision", nullable: true),
                    CentroidLng = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Iso2);
                });

            migrationBuilder.CreateTable(
                name: "CountrySearchRow",
                columns: table => new
                {
                    CountryIso2 = table.Column<string>(type: "text", nullable: false),
                    CountryIso3 = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "NewsSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Classification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    Categories = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    FirstDiscoveredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastDiscoveredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArticleCacheItems",
                columns: table => new
                {
                    ArticleCacheId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCacheItems", x => new { x.ArticleCacheId, x.ArticleId });
                    table.ForeignKey(
                        name: "FK_ArticleCacheItems_ArticleCaches_ArticleCacheId",
                        column: x => x.ArticleCacheId,
                        principalTable: "ArticleCaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleCacheItems_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CountryName = table.Column<string>(type: "text", nullable: false),
                    CountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Population = table.Column<long>(type: "bigint", nullable: false),
                    LocalName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_Countries_CountryIso2",
                        column: x => x.CountryIso2,
                        principalTable: "Countries",
                        principalColumn: "Iso2",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsSourceFeeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    EntryCount = table.Column<int>(type: "integer", nullable: true),
                    LatestEntry = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    HasFullContent = table.Column<bool>(type: "boolean", nullable: true),
                    ExternalLinkRatio = table.Column<double>(type: "double precision", nullable: true),
                    DistinctSources = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    LastEtag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSourceFeeds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsSourceFeeds_NewsSources_NewsSourceId",
                        column: x => x.NewsSourceId,
                        principalTable: "NewsSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CadenceDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 90),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveEmptyRuns = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryTargets_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscoveryTargets_Countries_CountryIso2",
                        column: x => x.CountryIso2,
                        principalTable: "Countries",
                        principalColumn: "Iso2",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscoveryJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveryTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    ErrorStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    Stats = table.Column<DiscoveryStatsDTO>(type: "jsonb", nullable: true),
                    Warnings = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveryJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveryJobs_DiscoveryTargets_DiscoveryTargetId",
                        column: x => x.DiscoveryTargetId,
                        principalTable: "DiscoveryTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsSourceScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    PollingTier = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SearchOccurrences = table.Column<int>(type: "integer", nullable: true),
                    MatchedQueries = table.Column<List<string>>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    DiscoveredAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    DiscoveryJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSourceScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsSourceScopes_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewsSourceScopes_Countries_CountryIso2",
                        column: x => x.CountryIso2,
                        principalTable: "Countries",
                        principalColumn: "Iso2",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewsSourceScopes_DiscoveryJobs_DiscoveryJobId",
                        column: x => x.DiscoveryJobId,
                        principalTable: "DiscoveryJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NewsSourceScopes_NewsSources_NewsSourceId",
                        column: x => x.NewsSourceId,
                        principalTable: "NewsSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCacheItems_ArticleId",
                table: "ArticleCacheItems",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ExpiresAt",
                table: "ArticleCaches",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCaches",
                columns: new[] { "ScopeKey", "Page" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Provider_ProviderArticleId",
                table: "Articles",
                columns: new[] { "Provider", "ProviderArticleId" },
                unique: true,
                filter: "\"ProviderArticleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_CountryIso2",
                table: "Cities",
                column: "CountryIso2");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Iso3",
                table: "Countries",
                column: "Iso3",
                unique: true,
                filter: "\"Iso3\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryJobs_DiscoveryTargetId",
                table: "DiscoveryJobs",
                column: "DiscoveryTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryJobs_Status_StartedAt",
                table: "DiscoveryJobs",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryTargets_CityId",
                table: "DiscoveryTargets",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryTargets_CountryIso2",
                table: "DiscoveryTargets",
                column: "CountryIso2",
                unique: true,
                filter: "\"CityId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryTargets_CountryIso2_CityId",
                table: "DiscoveryTargets",
                columns: new[] { "CountryIso2", "CityId" },
                unique: true,
                filter: "\"CityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveryTargets_IsEnabled_NextDueAt_Priority",
                table: "DiscoveryTargets",
                columns: new[] { "IsEnabled", "NextDueAt", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_IsActive_LastPolledAt",
                table: "NewsSourceFeeds",
                columns: new[] { "IsActive", "LastPolledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_NewsSourceId_Url",
                table: "NewsSourceFeeds",
                columns: new[] { "NewsSourceId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSources_Domain",
                table: "NewsSources",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSources_IsActive_Classification",
                table: "NewsSources",
                columns: new[] { "IsActive", "Classification" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_CityId",
                table: "NewsSourceScopes",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_CountryIso2_CityId_DiscoveryJobId",
                table: "NewsSourceScopes",
                columns: new[] { "CountryIso2", "CityId", "DiscoveryJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_CountryIso2_CityId_IsStale_PollingTier",
                table: "NewsSourceScopes",
                columns: new[] { "CountryIso2", "CityId", "IsStale", "PollingTier" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_DiscoveryJobId",
                table: "NewsSourceScopes",
                column: "DiscoveryJobId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_NewsSourceId_CountryIso2",
                table: "NewsSourceScopes",
                columns: new[] { "NewsSourceId", "CountryIso2" },
                unique: true,
                filter: "\"CityId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceScopes_NewsSourceId_CountryIso2_CityId",
                table: "NewsSourceScopes",
                columns: new[] { "NewsSourceId", "CountryIso2", "CityId" },
                unique: true,
                filter: "\"CityId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleCacheItems");

            migrationBuilder.DropTable(
                name: "CitySearchRow");

            migrationBuilder.DropTable(
                name: "CountrySearchRow");

            migrationBuilder.DropTable(
                name: "NewsSourceFeeds");

            migrationBuilder.DropTable(
                name: "NewsSourceScopes");

            migrationBuilder.DropTable(
                name: "ArticleCaches");

            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropTable(
                name: "DiscoveryJobs");

            migrationBuilder.DropTable(
                name: "NewsSources");

            migrationBuilder.DropTable(
                name: "DiscoveryTargets");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
