using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class SeoTermsAutofill : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"UPDATE RestaurantSeo s
INNER JOIN Restaurants r ON r.Id = s.ItemId
SET
 s.SearchTerms = r.Name
WHERE
 ISNULL(s.SearchTerms);");

            migrationBuilder.Sql(@"UPDATE DishSeo s
INNER JOIN Dishes d ON d.Id = s.ItemId
SET
 s.SearchTerms = CONCAT(JSON_VALUE(Name, ""$.Values[0].Value""), "" "", JSON_VALUE(Name, ""$.Values[1].Value""), "" "", JSON_VALUE(Name, ""$.Values[2].Value""))
WHERE
 ISNULL(s.SearchTerms);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
