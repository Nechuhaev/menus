using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class RestaurantRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestaurantRoles",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    RestaurantId = table.Column<string>(maxLength: 256, nullable: false),
                    UserId = table.Column<string>(maxLength: 256, nullable: false),
                    Role = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantRoles", x => x.Id);
                    table.ForeignKey(
                        "FK_Restaurants_RestaurantRoles_RestaurantId",
                        column: x => x.RestaurantId,
                        principalTable: "Restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        "FK_AspNetUsers_RestaurantRoles_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantRoles");
        }
    }
}
