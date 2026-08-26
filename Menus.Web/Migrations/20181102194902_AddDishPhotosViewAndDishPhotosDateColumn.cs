using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace Menus.Web.Migrations
{
    public partial class AddDishPhotosViewAndDishPhotosDateColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
               name: "Date",
               table: "DishPhotos",
               nullable: false,
               defaultValue: DateTime.Now);
            migrationBuilder.Sql(@"CREATE
                DEFINER = 'root'@'%'
                VIEW DishPhotoExtended
                AS
                SELECT 
                  dp.Id AS 'Id',
                  dp.UserId AS 'UserId',
                  dp.ImageUrl AS 'ImageUrl',
                  dp.Status AS 'Status',
                  dp.Index AS 'Index',
                  dp.Date AS 'Date',
                  dp.DishId AS 'DishId',
                  d.Name AS 'DishName',
                  r.Name AS 'RestaurantName',
                  anu.FirstName AS 'UserFirstName',
                  anu.LastName AS 'UserLastName'
                FROM DishPhotos dp
                LEFT JOIN Dishes d ON d.Id = dp.DishId
                LEFT JOIN Restaurants r ON r.Id = d.RestaurantId
                LEFT JOIN AspNetUsers anu ON anu.Id = dp.UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Date",
                table: "DishPhotos");
            migrationBuilder.Sql("DROP VIEW IF EXISTS DishPhotoExtended CASCADE;");
        }
    }
}
