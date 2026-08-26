using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishesRestaurantFK : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RestaurantId",
                table: "Dishes",
                maxLength: 256,
                nullable: false
            );
            migrationBuilder.AddForeignKey(
                name: "FK_Restaurant_Dishes_RestaurantId",
                table: "Dishes",
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
                table: "Dishes");
        }
    }
}
