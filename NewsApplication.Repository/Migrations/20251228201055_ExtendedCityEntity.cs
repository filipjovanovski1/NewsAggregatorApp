using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ExtendedCityEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Population",
                table: "CitySearchRow",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Population",
                table: "Cities",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Population",
                table: "CitySearchRow");

            migrationBuilder.DropColumn(
                name: "Population",
                table: "Cities");
        }
    }
}
