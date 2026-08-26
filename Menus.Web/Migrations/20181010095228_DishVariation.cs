using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishVariation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                table: "Dishes");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "Dishes");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "RestaurantInfo",
                nullable: false,
                defaultValue: "₴");

            migrationBuilder.CreateTable(
                name: "DishVariations",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    DishId = table.Column<string>(maxLength: 256, nullable: false),
                    Price = table.Column<decimal>(nullable: false),
                    Weight = table.Column<int>(nullable: false),
                    Type = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DishVariations", x => x.Id);

                    table.ForeignKey(
                        "FK_Dishes_DishVariations_DishId",
                        column: x => x.DishId,
                        principalTable: "Dishes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DishVariations");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "RestaurantInfo");

            migrationBuilder.AddColumn<string>(
                name: "Price",
                table: "Dishes",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Weight",
                table: "Dishes",
                nullable: true);
        }
    }
}
