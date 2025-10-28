using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class CreateArticleCacheItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleScopes");

            migrationBuilder.RenameColumn(
                name: "Link",
                table: "Article",
                newName: "Url");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "Article",
                newName: "Provider");

            migrationBuilder.AddColumn<List<string>>(
                name: "Categories",
                table: "Article",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InsertedAt",
                table: "Article",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateTable(
                name: "ArticleCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeKey = table.Column<string>(type: "text", nullable: false),
                    Page = table.Column<int>(type: "integer", nullable: false),
                    NextPageToken = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArticleCacheItem",
                columns: table => new
                {
                    ArticleCacheId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleCacheItem", x => new { x.ArticleCacheId, x.ArticleId });
                    table.ForeignKey(
                        name: "FK_ArticleCacheItem_ArticleCache_ArticleCacheId",
                        column: x => x.ArticleCacheId,
                        principalTable: "ArticleCache",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleCacheItem_Article_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Article",
                        principalColumn: "ArticleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCache_ExpiresAt",
                table: "ArticleCache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCache_ScopeKey_Page",
                table: "ArticleCache",
                columns: new[] { "ScopeKey", "Page" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCacheItem_ArticleId",
                table: "ArticleCacheItem",
                column: "ArticleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleCacheItem");

            migrationBuilder.DropTable(
                name: "ArticleCache");

            migrationBuilder.DropColumn(
                name: "Categories",
                table: "Article");

            migrationBuilder.DropColumn(
                name: "InsertedAt",
                table: "Article");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Article",
                newName: "Link");

            migrationBuilder.RenameColumn(
                name: "Provider",
                table: "Article",
                newName: "Category");

            migrationBuilder.CreateTable(
                name: "ArticleScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    OtherValue = table.Column<string>(type: "character varying(168)", maxLength: 168, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleScopes", x => x.Id);
                    table.CheckConstraint("CK_ArticleScope_ExactlyOneKey", "(CASE WHEN \"CityId\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"CountryIso2\" IS NULL THEN 0 ELSE 1 END) + (CASE WHEN \"OtherValue\" IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_ArticleScopes_Article_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Article",
                        principalColumn: "ArticleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleScopes_ArticleId_CityId",
                table: "ArticleScopes",
                columns: new[] { "ArticleId", "CityId" },
                unique: true,
                filter: "\"CityId\" IS NOT NULL AND \"Kind\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleScopes_ArticleId_CountryIso2",
                table: "ArticleScopes",
                columns: new[] { "ArticleId", "CountryIso2" },
                unique: true,
                filter: "\"CountryIso2\" IS NOT NULL AND \"Kind\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleScopes_ArticleId_OtherValue",
                table: "ArticleScopes",
                columns: new[] { "ArticleId", "OtherValue" },
                unique: true,
                filter: "\"OtherValue\" IS NOT NULL AND \"Kind\" = 3");
        }
    }
}
