using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class CompleteRestaurantAndGps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TABLE menus.RestaurantGps (
  RestaurantId varchar(255) NOT NULL,
  Latitude decimal(10, 6) DEFAULT NULL,
  Longitude decimal(10, 6) DEFAULT NULL,
  PRIMARY KEY (RestaurantId)
)
ENGINE = INNODB,
AVG_ROW_LENGTH = 287,
CHARACTER SET utf8,
COLLATE utf8_general_ci;

ALTER TABLE menus.RestaurantGps
ADD INDEX IDX_RestaurantGps_Latitude (Latitude);

ALTER TABLE menus.RestaurantGps
ADD INDEX IDX_RestaurantGps_Longitude (Longitude);

ALTER TABLE menus.RestaurantGps
ADD CONSTRAINT FK_RestaurantGps_RestaurantId FOREIGN KEY (RestaurantId)
REFERENCES menus.Restaurants (Id) ON DELETE CASCADE;");

            migrationBuilder.Sql(@"CREATE 
	DEFINER = 'root'@'%'
TRIGGER menus.GpsTableInsert
	AFTER INSERT
	ON menus.RestaurantInfo
	FOR EACH ROW
BEGIN
  INSERT INTO RestaurantGps
    VALUES(NEW.RestaurantId,
    CAST(NEW.GpsLatitude AS decimal(10, 6)),
    CAST(NEW.GpsLongitude AS decimal(10, 6)));
END");

            migrationBuilder.Sql(@"CREATE 
	DEFINER = 'root'@'%'
TRIGGER menus.GpsTableUpdate
	AFTER UPDATE
	ON menus.RestaurantInfo
	FOR EACH ROW
BEGIN
  UPDATE RestaurantGps
    SET
    Latitude = CAST(NEW.GpsLatitude AS decimal(10, 6)),
    Longitude = CAST(NEW.GpsLongitude AS decimal(10, 6))
  WHERE RestaurantId = NEW.RestaurantId;
END");

            migrationBuilder.Sql(@"INSERT INTO RestaurantGps
(
  RestaurantId
 ,Latitude
 ,Longitude
)
SELECT ri.RestaurantId, CAST(ri.GpsLatitude AS decimal(10, 6)), CAST(ri.GpsLongitude AS decimal(10, 6))
FROM RestaurantInfo ri;");

            migrationBuilder.Sql(@"CREATE
DEFINER = 'root'@'%'
VIEW RestaurantsWithInfoAndPositions
AS
SELECT
  `Restaurants`.`Id` AS `Id`,
  `Restaurants`.`Name` AS `Name`,
  `Restaurants`.`Email` AS `Email`,
  `Restaurants`.`Phone` AS `Phone`,
  `Restaurants`.`Address` AS `Address`,
  `Restaurants`.`Status` AS `Status`,
  `Restaurants`.`City` AS `City`,
  `RestaurantInfo`.`Photos` AS `Photos`,
  `RestaurantInfo`.`Website` AS `Website`,
  `RestaurantInfo`.`OpenHours` AS `OpenHours`,
  `RestaurantInfo`.`Contacts` AS `Contacts`,
  `rg`.`Latitude` AS `GpsLatitude`,
  `rg`.`Longitude` AS `GpsLongitude`,
  `RestaurantInfo`.`ShortInfo` AS `ShortInfo`,
  `RestaurantInfo`.`FullInfo` AS `FullInfo`,
  `RestaurantInfo`.`Cuisine` AS `Cuisine`,
  `RestaurantInfo`.`Currency` AS `Currency`,
  `RestaurantInfo`.`UserAllowedAddDishes` AS `UserAllowedAddDishes`,
  `RestaurantInfo`.`UserAllowedAddMenu` AS `UserAllowedAddMenu`,
  `RestaurantInfo`.`UserAllowedOpenMenu` AS `UserAllowedOpenMenu`,
  CAST(NULL AS decimal(10, 6)) AS `Distance`,
  `rs`.`Url` AS `Url`,
  (SELECT
      COUNT(`Ratings`.`Id`)
    FROM `Ratings`
    WHERE `Ratings`.`RestaurantId` = `Restaurants`.`Id`) AS `RatingsCount`
FROM (((`RestaurantInfo`
  JOIN `Restaurants`
    ON (`RestaurantInfo`.`RestaurantId` = `Restaurants`.`Id`))
  JOIN `RestaurantGps` `rg`
    ON (`Restaurants`.`Id` = `rg`.`RestaurantId`))
  JOIN `RestaurantSeo` `rs`
    ON (`rs`.`ItemId` = `rg`.`RestaurantId`));");

            migrationBuilder.Sql(@"CREATE DEFINER = 'root'@'%'
PROCEDURE menus.GetNearestRestaurants(IN latitude DECIMAL(10,6), IN longitude DECIMAL(10,6), IN radius INT, IN take INT)
  READS SQL DATA
BEGIN
SELECT
  rwiap.Id,
  rwiap.Name,
  rwiap.Email,
  rwiap.Phone,
  rwiap.Address,
  rwiap.Status,
  rwiap.City,
  rwiap.Photos,
  rwiap.Website,
  rwiap.OpenHours,
  rwiap.Contacts,
  rwiap.GpsLatitude,
  rwiap.GpsLongitude,
  rwiap.ShortInfo,
  rwiap.FullInfo,
  rwiap.Cuisine,
  rwiap.Currency,
  rwiap.RatingsCount,
  rwiap.UserAllowedAddDishes,
  rwiap.UserAllowedAddMenu,
  rwiap.UserAllowedOpenMenu,
  rwiap.Url,(
    6371.04 * acos (
      cos ( radians(latitude) )
      * cos( radians( rwiap.GpsLatitude ) )
      * cos( radians( rwiap.GpsLongitude ) - radians(longitude) )
      + sin ( radians(latitude) )
      * sin( radians( rwiap.GpsLatitude ) )
    )
  ) AS Distance
FROM RestaurantsWithInfoAndPositions rwiap
HAVING Distance < radius
ORDER BY Distance
LIMIT 0 , take;
END");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
