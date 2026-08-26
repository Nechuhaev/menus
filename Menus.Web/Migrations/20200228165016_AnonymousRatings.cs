using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class AnonymousRatings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnonymousRatings",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    DishId = table.Column<string>(nullable: false, maxLength: 256),
                    Rate = table.Column<int>(nullable: false),
                    UserIp = table.Column<string>(nullable: false),
                    DeviceId = table.Column<string>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymousRatings", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_AnonymousRatings_Dishes_DishId",
                table: "AnonymousRatings",
                column: "DishId",
                principalTable: "Dishes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnonymousRatings_Dishes_DishId",
                table: "AnonymousRatings");

            migrationBuilder.DropTable(
                name: "AnonymousRatings");
        }
    }
}
