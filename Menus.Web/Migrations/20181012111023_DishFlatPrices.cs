using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishFlatPrices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPriceMultiple",
                table: "Dishes",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Dishes",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Weight",
                table: "Dishes",
                nullable: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPriceMultiple",
                table: "Dishes");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Dishes");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "Dishes");
        }
    }
}
