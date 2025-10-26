using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddIso3Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Iso3",
                table: "Countries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true
                );

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Iso3",
                table: "Countries",
                column: "Iso3",
                unique: true,
                filter: "\"Iso3\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Countries_Iso3",
                table: "Countries");

            migrationBuilder.DropColumn(
                name: "Iso3",
                table: "Countries");
        }
    }
}
