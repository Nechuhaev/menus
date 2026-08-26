using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class Cities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Name = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.DropColumn(
                name: "City",
                table: "Restaurants"
            );

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Restaurants",
                nullable: true,
                maxLength: 256
            );

            migrationBuilder.AddForeignKey(
                name: "FK_City_Restaurant_City",
                table: "Restaurants",
                column: "City",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull
           );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cities");
        }
    }
}
