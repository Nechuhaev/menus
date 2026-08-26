using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishesWithSeoAndRatings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DishesWithSeoAndRatings",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    RestaurantId = table.Column<string>(nullable: true),
                    PhotoLink = table.Column<string>(nullable: true),
                    Price = table.Column<string>(nullable: true),
                    Weight = table.Column<string>(nullable: true),
                    Name = table.Column<string>(nullable: true),
                    ShortDescription = table.Column<string>(nullable: true),
                    Ingredients = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    Url = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    SeoDescription = table.Column<string>(nullable: true),
                    Keywords = table.Column<string>(nullable: true),
                    SeoText = table.Column<string>(nullable: true),
                    Rating = table.Column<int>(nullable: false),
                    RatingCount = table.Column<int>(nullable: false),
                    UserRating = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DishesWithSeoAndRatings", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DishesWithSeoAndRatings");
        }
    }
}
