using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishesAndDishCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Dishes",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    PhotoLink = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Price = table.Column<string>(nullable: false),
                    Weight = table.Column<string>(nullable: false),
                    ShortDescription = table.Column<string>(nullable: false),
                    Ingredients = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dishes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DishCategories",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    MenuCategoryId = table.Column<string>(maxLength: 256, nullable: false),
                    DishId = table.Column<string>(maxLength: 256, nullable: false),
                    Index = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DishCategories", x => x.Id);
                    table.ForeignKey(
                        "FK_MenuCategories_DishCategories_MenuCategoryId",
                        column: x => x.MenuCategoryId,
                        principalTable: "MenuCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        "FK_Dishes_DishCategories_DishId",
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
                name: "DishCategories");

            migrationBuilder.DropTable(
                name: "Dishes");
        }
    }
}
