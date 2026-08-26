using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class RestaurantInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestaurantInfo",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    RestaurantId = table.Column<string>(maxLength: 256, nullable: false),
                    Photos = table.Column<string>(nullable: true),
                    Website = table.Column<string>(nullable: true),
                    OpenHours = table.Column<string>(nullable: true),
                    Contacts = table.Column<string>(nullable: true),
                    GpsLatitude = table.Column<string>(nullable: true),
                    GpsLongitude = table.Column<string>(nullable: true),
                    ShortInfo = table.Column<string>(nullable: true),
                    FullInfo = table.Column<string>(nullable: true),
                    Cuisine = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantInfo", x => x.Id);
                    table.ForeignKey(
                        "FK_Restaurant_RestaurantInfo_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantInfo");
        }
    }
}
