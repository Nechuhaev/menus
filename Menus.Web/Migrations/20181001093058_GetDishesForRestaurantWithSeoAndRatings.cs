using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class GetDishesForRestaurantWithSeoAndRatings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
FUNCTION menus.GetDishRating(dishIdInput VARCHAR(255))
  RETURNS int(11)
  READS SQL DATA
BEGIN
DECLARE rating INT DEFAULT 0;
SELECT CEIL(AVG(r.Rate)) INTO rating FROM Ratings r WHERE dishId = dishIdInput;
RETURN IFNULL(rating, 0);
END");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
FUNCTION menus.GetDishRatingForUser(userId VARCHAR(255), dishId VARCHAR(255))
  RETURNS int(11)
  READS SQL DATA
BEGIN
DECLARE rating INT DEFAULT 0;
SELECT r.Rate INTO rating FROM Ratings r WHERE r.UserId = userId AND dishId = dishId LIMIT 1;
RETURN IFNULL(rating, 0);
END");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
FUNCTION menus.GetDishRatingsCount(dishIdInput VARCHAR(255))
  RETURNS int(11)
  READS SQL DATA
BEGIN
DECLARE rating INT DEFAULT 0;
SELECT COUNT(r.Rate) INTO rating FROM Ratings r WHERE dishId = dishIdInput;
RETURN IFNULL(rating, 0);
END");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE menus.GetDishesForRestaurantWithSeoAndRatings(IN restaurantId VARCHAR(255), IN userId VARCHAR(255))
  READS SQL DATA
BEGIN
SELECT
  `Dishes`.`Id` AS `Id`,
  `Dishes`.`PhotoLink` AS `PhotoLink`,
  `Dishes`.`Name` AS `Name`,
  `Dishes`.`Price` AS `Price`,
  `Dishes`.`Weight` AS `Weight`,
  `Dishes`.`WeightType` AS `WeightType`,
  `Dishes`.`ShortDescription` AS `ShortDescription`,
  `Dishes`.`Ingredients` AS `Ingredients`,
  `Dishes`.`Description` AS `Description`,
  `Dishes`.`RestaurantId` AS `RestaurantId`,
  `DishSeo`.`Url` AS `Url`,
  `DishSeo`.`Title` AS `Title`,
  `DishSeo`.`Description` AS `SeoDescription`,
  `DishSeo`.`Keywords` AS `Keywords`,
  `DishSeo`.`SeoText` AS `SeoText`,
  `menus`.`GetDishRating`(`Dishes`.`Id`) AS `Rating`,
  `menus`.`GetDishRatingsCount`(`Dishes`.`Id`) AS `RatingCount`,
  IFNULL(`Ratings`.`Rate`, 0) AS `UserRating`
FROM (`Dishes`
  JOIN `DishSeo`
    ON (`DishSeo`.`ItemId` = `Dishes`.`Id`)
  LEFT JOIN `Ratings`
    ON (`Ratings`.`DishId` = `Dishes`.`Id`) AND (`Ratings`.`UserId` = userId))
WHERE `Dishes`.`RestaurantId` = restaurantId;
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP PROCEDURE menus.GetDishesForRestaurantWithSeoAndRatings;");
            migrationBuilder.Sql(@"DROP FUNCTION menus.GetDishRatingsCount;");
            migrationBuilder.Sql(@"DROP FUNCTION menus.GetDishRatingForUser;");
            migrationBuilder.Sql(@"DROP FUNCTION menus.GetDishRating;");
        }
    }
}
