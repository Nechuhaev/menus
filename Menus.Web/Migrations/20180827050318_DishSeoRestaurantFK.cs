using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishSeoRestaurantFK : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RestaurantId",
                table: "DishSeo",
                maxLength: 256, nullable: false);

            migrationBuilder.AddForeignKey(
                name: "FK_Restaurant_DishSeo_RestaurantId",
                table: "DishSeo",
                column: "RestaurantId",
                principalTable: "Restaurants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
           );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RestaurantId",
                table: "DishSeo");
        }
    }
}
