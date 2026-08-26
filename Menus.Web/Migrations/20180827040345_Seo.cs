using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class Seo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DishSeo",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    ItemId = table.Column<string>(maxLength: 256, nullable: false),
                    Url = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    Keywords = table.Column<string>(nullable: true),
                    SeoText = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DishSeo", x => x.Id);
                    table.ForeignKey(
                        "FK_Dishes_DishSeo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Dishes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });

            migrationBuilder.CreateTable(
                name: "RestaurantSeo",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    ItemId = table.Column<string>(maxLength: 256, nullable: false),
                    Url = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    Keywords = table.Column<string>(nullable: true),
                    SeoText = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantSeo", x => x.Id);
                    table.ForeignKey(
                        "FK_Restaurants_RestaurantSeo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DishSeo");

            migrationBuilder.DropTable(
                name: "RestaurantSeo");
        }
    }
}
