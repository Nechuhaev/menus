using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class AddUserRatingsProcedure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
                PROCEDURE menus.GetUserRatings(IN userId varchar(255), IN status varchar(255))
                BEGIN
                    SELECT
                      rate.Id,
                      rate.RestaurantId,
                      rate.DishId,
                      rate.UserId,
                      rate.Rate,
                      rate.UpdateDate,
                      rate.Text,
                      rate.Status,
                      d.Name AS 'DishName',
                      r.Name AS 'RestaurantName',
                      r.Address AS 'RestaurantAddress',
                      anu.FirstName AS 'UserFirstName',
                      anu.LastName AS 'UserLastName',
                      d.PhotoLink AS 'DishImageUrl'
                      FROM Ratings rate
                      INNER JOIN Dishes d ON d.Id = rate.DishId
                      INNER JOIN Restaurants r ON r.Id = rate.RestaurantId
                      INNER JOIN AspNetUsers anu ON anu.Id = rate.UserId
                      WHERE rate.UserId = IFNULL(NULLIF(userId,''), rate.UserId) AND rate.Status = IFNULL(CONVERT(NULLIF(status,''), int), rate.Status);
                END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE menus.GetUserRatings;");
        }
    }
}
