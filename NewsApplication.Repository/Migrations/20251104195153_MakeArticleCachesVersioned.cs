using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class MakeArticleCachesVersioned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCaches");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ScopeKey_Page_CreatedAt",
                table: "ArticleCaches",
                columns: new[] { "ScopeKey", "Page", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArticleCaches_ScopeKey_Page_CreatedAt",
                table: "ArticleCaches");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCaches",
                columns: new[] { "ScopeKey", "Page" },
                unique: true);
        }
    }
}
