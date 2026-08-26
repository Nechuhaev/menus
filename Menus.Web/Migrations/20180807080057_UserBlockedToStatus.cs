using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class UserBlockedToStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Blocked",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "AspNetUsers",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<bool>(
                name: "Blocked",
                table: "AspNetUsers",
                nullable: false,
                defaultValue: false);
        }
    }
}
