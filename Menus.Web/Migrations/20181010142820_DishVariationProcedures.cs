using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishVariationProcedures : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
FUNCTION menus.IsDishHaveMultipleVariations(dishId VARCHAR(255))
  RETURNS bit(1)
BEGIN
DECLARE isDishHaveMultipleVariations BIT DEFAULT 0;
SELECT IF(COUNT(*) > 1, 1, 0) INTO isDishHaveMultipleVariations FROM DishVariations d WHERE d.DishId = dishId;
RETURN IFNULL(isDishHaveMultipleVariations, 0);
END");
            migrationBuilder.Sql(@"DROP PROCEDURE menus.GetDishesForRestaurantWithSeoAndRatings;");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE menus.GetDishesForRestaurantWithSeoAndRatings(IN restaurantId VARCHAR(255), IN userId VARCHAR(255))
  READS SQL DATA
BEGIN
SELECT
  `Dishes`.`Id` AS `Id`,
  `Dishes`.`PhotoLink` AS `PhotoLink`,
  `Dishes`.`Name` AS `Name`,
  IFNULL(`DishVariations`.`Price`, 0.0) AS `Price`,
  CONCAT(IFNULL(`DishVariations`.`Weight`, 0.0), CONCAT("" "", IFNULL(`DishVariations`.`Type`, ""g""))) AS `Weight`,
  `menus`.`IsDishHaveMultipleVariations`(`Dishes`.`Id`) AS `IsPriceMultiple`,
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
  LEFT JOIN `DishVariations`
    ON (`DishVariations`.`DishId` = `Dishes`.`Id`) AND (`DishVariations`.`Price` = (
      SELECT MIN(dv.Price)
      FROM `DishVariations` dv
      WHERE `dv`.`DishId` = `Dishes`.`Id`
    ))
WHERE `Dishes`.`RestaurantId` = restaurantId;
END");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE menus.GetDishWithSeoAndRatings(IN dishId VARCHAR(255), IN userId VARCHAR(255))
  READS SQL DATA
BEGIN
SELECT
  `Dishes`.`Id` AS `Id`,
  `Dishes`.`PhotoLink` AS `PhotoLink`,
  `Dishes`.`Name` AS `Name`,
  IFNULL(`DishVariations`.`Price`, 0.0) AS `Price`,
  CONCAT(IFNULL(`DishVariations`.`Weight`, 0.0), CONCAT("" "", IFNULL(`DishVariations`.`Type`, ""g""))) AS `Weight`,
  `menus`.`IsDishHaveMultipleVariations`(`Dishes`.`Id`) AS `IsPriceMultiple`,
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
  LEFT JOIN `DishVariations`
    ON (`DishVariations`.`DishId` = `Dishes`.`Id`) AND (`DishVariations`.`Price` = (
      SELECT MIN(dv.Price)
      FROM `DishVariations` dv
      WHERE `dv`.`DishId` = `Dishes`.`Id`
    ))
WHERE `Dishes`.`Id` = dishId
LIMIT 1;
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP PROCEDURE menus.GetDishWithSeoAndRatings;");
            migrationBuilder.Sql(@"DROP PROCEDURE menus.GetDishesForRestaurantWithSeoAndRatings;");
            migrationBuilder.Sql(@"DROP FUNCTION menus.IsDishHaveMultipleVariations;");

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
    }
}
