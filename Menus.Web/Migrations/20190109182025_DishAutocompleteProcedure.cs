using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class DishAutocompleteProcedure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE GetSearchDishWithSeoAndRatings (IN `query` varchar(255), IN `latitude` decimal(10, 6), IN `longitude` decimal(10, 6))
READS SQL DATA
BEGIN
  SELECT
    `Dishes`.`Id` AS `Id`,
    `Dishes`.`Name` AS `Name`,
    `RestaurantsWithInfoAndPositions`.`Name` AS `RestaurantName`,
    `RestaurantsWithInfoAndPositions`.`Url` AS `RestaurantUrl`,
    IFNULL(
    6371.04 * ACOS(
    COS(RADIANS(latitude))
    * COS(RADIANS(`RestaurantsWithInfoAndPositions`.GpsLatitude))
    * COS(RADIANS(`RestaurantsWithInfoAndPositions`.GpsLongitude) - RADIANS(longitude))
    + SIN(RADIANS(latitude))
    * SIN(RADIANS(`RestaurantsWithInfoAndPositions`.GpsLatitude))
    ), 0.0) AS Distance,
    `DishSeo`.`Url` AS `Url`,
    `DishSeo`.`SearchTerms` AS `SearchTerms`,
    `menus`.`GetDishRating`(`Dishes`.`Id`) AS `Rating`,
    `menus`.`GetDishRatingFloat`(`Dishes`.`Id`) AS `RatingFloat`
  FROM (`Dishes`
    LEFT JOIN `DishSeo`
      ON (`DishSeo`.`ItemId` = `Dishes`.`Id`)
    LEFT JOIN `RestaurantsWithInfoAndPositions`
      ON (`RestaurantsWithInfoAndPositions`.`Id` = `Dishes`.`RestaurantId`))
  WHERE `DishSeo`.`SearchTerms` LIKE CONCAT(""%"", query, ""%"")
  GROUP BY `Dishes`.`RestaurantId`
  ORDER BY Distance DESC, RatingFloat DESC
  LIMIT 10;
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
