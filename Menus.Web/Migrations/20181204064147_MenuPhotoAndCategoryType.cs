using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class MenuPhotoAndCategoryType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryType",
                table: "MenuCategories",
                maxLength: 256, nullable: true);

            migrationBuilder.CreateTable(
                name: "MenuCategoryTypes",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuCategoryTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuPhotos",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    RestaurantId = table.Column<string>(maxLength: 256, nullable: false),
                    Photos = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPhotos", x => x.Id);
                    table.ForeignKey(
                        "FK_Restaurant_MenuPhotos_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });

            migrationBuilder.AddForeignKey(
                name: "FK_MenuCategoryType_MenuCategories_CategoryType",
                table: "MenuCategories",
                column: "CategoryType",
                principalTable: "MenuCategoryTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MenuCategoryTypes");

            migrationBuilder.DropTable(
                name: "MenuPhotos");

            migrationBuilder.DropColumn(
                name: "CategoryType",
                table: "MenuCategories");
        }
    }
}
