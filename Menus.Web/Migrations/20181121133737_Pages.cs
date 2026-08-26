using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class Pages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<string>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    ShortContent = table.Column<string>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    Category = table.Column<int>(nullable: false),
                    Status = table.Column<int>(nullable: false),
                    Url = table.Column<string>(nullable: false),
                    SeoTitle = table.Column<string>(nullable: true),
                    SeoDescription = table.Column<string>(nullable: true),
                    SeoKeywords = table.Column<string>(nullable: true),
                    SeoText = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pages");
        }
    }
}
