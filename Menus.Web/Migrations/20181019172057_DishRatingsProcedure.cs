using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishRatingsProcedure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
FUNCTION menus.GetRatingsCountForUser(userId VARCHAR(255))
  RETURNS int(11)
  READS SQL DATA
BEGIN
DECLARE ratings INT DEFAULT 0;
SELECT COUNT(*) INTO ratings FROM Ratings r WHERE r.UserId = userId;
RETURN IFNULL(ratings, 0);
END");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE menus.GetRatingsForDishWithUsers(IN dishId VARCHAR(255))
  READS SQL DATA
BEGIN
SELECT
  Ratings.Id AS Id,
  Ratings.RestaurantId AS RestaurantId,
  Ratings.DishId AS DishId,
  Ratings.UserId AS UserId,
  Ratings.Rate AS Rate,
  Ratings.UpdateDate AS UpdateDate,
  Ratings.Text AS Text,
  AspNetUsers.FirstName AS UserFirstName,
  AspNetUsers.LastName AS UserLastName,
  AspNetUsers.ImageUrl AS UserPhoto,
  GetRatingsCountForUser(AspNetUsers.Id) AS UserRatingsCount
FROM Ratings
  JOIN AspNetUsers
    ON (Ratings.UserId = AspNetUsers.Id)
WHERE Ratings.DishId = dishId;
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE menus.GetRatingsForDishWithUsers;");
            migrationBuilder.Sql("DROP FUNCTION menus.GetRatingsCountForUser;");
        }
    }
}
