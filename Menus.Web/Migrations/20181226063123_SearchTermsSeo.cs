using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class SearchTermsSeo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchTerms",
                table: "RestaurantSeo",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchTerms",
                table: "DishSeo",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchTerms",
                table: "RestaurantSeo");

            migrationBuilder.DropColumn(
                name: "SearchTerms",
                table: "DishSeo");
        }
    }
}
