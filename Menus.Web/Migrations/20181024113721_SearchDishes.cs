using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class SearchDishes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE
DEFINER = 'root'@'%'
VIEW SearchDishes
AS
SELECT
  `Dishes`.`Id` AS `Id`,
  `Dishes`.`PhotoLink` AS `PhotoLink`,
  `Dishes`.`Name` AS `Name`,
  `Dishes`.`IsPriceMultiple` AS `IsPriceMultiple`,
  `Dishes`.`Price` AS `Price`,
  `Dishes`.`Weight` AS `Weight`,
  `Dishes`.`WeightType` AS `WeightType`,
  `Restaurants`.`Name` AS `RestaurantName`,
  `Restaurants`.`Address` AS `RestaurantAddress`,
  `Dishes`.`ShortDescription` AS `ShortDescription`,
  `DishSeo`.`Url` AS `DishUrl`,
  `RestaurantSeo`.`Url` AS `RestaurantUrl`,
  `RestaurantInfo`.`Currency` AS `RestaurantCurrency`,
  `GetDishRatingsCount`(`Dishes`.`Id`) AS `RatingsCount`,
  `GetDishRating`(`Dishes`.`Id`) AS `Rating`
FROM ((((`Dishes`
  JOIN `Restaurants`
    ON (`Dishes`.`RestaurantId` = `Restaurants`.`Id`))
  JOIN `DishSeo`
    ON (`DishSeo`.`ItemId` = `Dishes`.`Id`))
  JOIN `RestaurantSeo`
    ON (`RestaurantSeo`.`ItemId` = `Restaurants`.`Id`))
  JOIN `RestaurantInfo`
    ON (`RestaurantInfo`.`RestaurantId` = `Restaurants`.`Id`));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS SearchDishes CASCADE;");
        }
    }
}
