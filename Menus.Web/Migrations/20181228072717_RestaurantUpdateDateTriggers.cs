using Microsoft.EntityFrameworkCore.Migrations;

namespace Menus.Web.Migrations
{
    public partial class RestaurantUpdateDateTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE
DEFINER = 'root'@'%'
TRIGGER DishDeleteRestaurantDate
BEFORE DELETE
ON Dishes
FOR EACH ROW
BEGIN
  UPDATE Restaurants
  SET UpdateDate = NOW()
  WHERE Id = OLD.RestaurantId;
END;");

            migrationBuilder.Sql(@"
CREATE
DEFINER = 'root'@'%'
TRIGGER DishInsertRestaurantDate
AFTER INSERT
ON Dishes
FOR EACH ROW
BEGIN
  UPDATE Restaurants
  SET UpdateDate = NOW()
  WHERE Id = NEW.RestaurantId;
END;");

            migrationBuilder.Sql(@"CREATE
DEFINER = 'root'@'%'
TRIGGER DishUpdateRestaurantDate
AFTER UPDATE
ON Dishes
FOR EACH ROW
BEGIN
  UPDATE Restaurants
  SET UpdateDate = NOW()
  WHERE Id = NEW.RestaurantId;
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS DishDeleteRestaurantDate;");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS DishInsertRestaurantDate;");

            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS DishUpdateRestaurantDate;");
        }
    }
}
