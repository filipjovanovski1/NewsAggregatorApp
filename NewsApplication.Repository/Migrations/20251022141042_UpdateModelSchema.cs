using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Article",
                columns: table => new
                {
                    ArticleId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Link = table.Column<string>(type: "text", nullable: false),
                    PublishedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Article", x => x.ArticleId);
                });

            migrationBuilder.CreateTable(
                name: "ArticleScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: true),
                    CountryIso2 = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleScopes");

            migrationBuilder.DropTable(
                name: "Article");
        }
    }
}
