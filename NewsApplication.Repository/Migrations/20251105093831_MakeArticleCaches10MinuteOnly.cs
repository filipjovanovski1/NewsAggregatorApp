using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class MakeArticleCaches10MinuteOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArticleCaches_ScopeKey_Page_CreatedAt",
                table: "ArticleCaches");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ArticleCaches");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCaches",
                columns: new[] { "ScopeKey", "Page" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCaches");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ArticleCaches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleCaches_ScopeKey_Page_CreatedAt",
                table: "ArticleCaches",
                columns: new[] { "ScopeKey", "Page", "CreatedAt" });
        }
    }
}
