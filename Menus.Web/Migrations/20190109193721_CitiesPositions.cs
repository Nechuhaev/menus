using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class CitiesPositions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Latitude",
                table: "Cities",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Longitude",
                table: "Cities",
                nullable: true);

            migrationBuilder.Sql(@"UPDATE Cities 
SET
  Latitude = '50.450982'
 ,Longitude = '30.523292'
WHERE
  Id = 'e2655e24-92dc-4179-abf6-c3908278a4de';

UPDATE Cities 
SET
  Latitude = '49.841835'
 ,Longitude = '24.031607'
WHERE
  Id = 'dfd4e011-3e6d-4057-bc6a-f4f47235dde0';

UPDATE Cities 
SET
  Latitude = '50.005699'
 ,Longitude = '36.229185'
WHERE
  Id = '58cda6f9-1b4e-4819-8dae-c07ea318fcb1';

UPDATE Cities 
SET
  Latitude = '46.478674'
 ,Longitude = '30.741420'
WHERE
  Id = '96423e36-6281-41d4-aa02-f6a187f7eb52';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Cities");
        }
    }
}
