using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class RestaurantInfoUserSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UserAllowedAddDishes",
                table: "RestaurantInfo",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UserAllowedAddMenu",
                table: "RestaurantInfo",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UserAllowedOpenMenu",
                table: "RestaurantInfo",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserAllowedAddDishes",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "UserAllowedAddMenu",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "UserAllowedOpenMenu",
                table: "RestaurantInfo");
        }
    }
}
